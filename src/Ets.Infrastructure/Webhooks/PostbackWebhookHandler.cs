// src/Ets.Infrastructure/Webhooks/PostbackWebhookHandler.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Ets.Application.Interfaces;
using Ets.Application.UseCases.TeamPlus;
using Ets.Application.UseCases.Webhooks;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Webhooks;

/// <summary>
/// Postback Webhook 處理器（§7.2）— 移至 Infrastructure 層（依賴 AppDbContext）
/// </summary>
public sealed class PostbackWebhookHandler
{
    private readonly AppDbContext _db;
    private readonly IDashboardNotifier _notifier;
    private readonly ILogger<PostbackWebhookHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PostbackWebhookHandler(
        AppDbContext db,
        IDashboardNotifier notifier,
        ILogger<PostbackWebhookHandler> logger)
    {
        _db       = db;
        _notifier = notifier;
        _logger   = logger;
    }

    public async Task HandleAsync(
        PostbackWebhookBody body,
        string rawPayload,
        bool signatureValid,
        CancellationToken ct)
    {
        foreach (var ev in body.Events)
        {
            if (!string.Equals(ev.Type, "postback", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Webhook 略過非 postback 事件：type={Type}", ev.Type);
                continue;
            }

            await ProcessPostbackEventAsync(ev, rawPayload, signatureValid, ct);
        }
    }

    private async Task ProcessPostbackEventAsync(
        WebhookEvent ev,
        string rawPayload,
        bool signatureValid,
        CancellationToken ct)
    {
        var userId = ev.Source.UserId;

        // ── 計算冪等鍵（§7.1）────────────────────────────────────
        var idempotencyKey = ComputeIdempotencyKey(ev);

        // ── 查 WebhookInbox：已存在 → 跳過 ───────────────────────
        var exists = await _db.WebhookInboxes
            .AnyAsync(w =>
                w.Source == "teamplus" &&
                w.ExternalMessageId == idempotencyKey, ct);

        if (exists)
        {
            _logger.LogInformation(
                "Postback 冪等跳過：UserId={UserId}, Key={Key}", userId, idempotencyKey);
            return;
        }

        // ── INSERT WebhookInbox ───────────────────────────────────
        var inbox = new WebhookInbox
        {
            Source            = "teamplus",
            ExternalMessageId = idempotencyKey,
            Account           = userId,
            RawPayload        = rawPayload,
            SignatureValid    = signatureValid,
            ReceivedAt        = DateTime.UtcNow
        };
        _db.WebhookInboxes.Add(inbox);

        // ── 解析 postback.data ────────────────────────────────────
        if (ev.Postback?.Data is null)
        {
            _logger.LogWarning("Postback.Data 為空：UserId={UserId}", userId);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var query    = HttpUtility.ParseQueryString(ev.Postback.Data);
        var eventId  = query["id"];
        var feedback = query["feedback"];

        if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(feedback))
        {
            _logger.LogWarning("Postback.Data 解析失敗：Data={Data}", ev.Postback.Data);
            await _db.SaveChangesAsync(ct);
            return;
        }

        inbox.EventId = eventId;

        // ── 載入 EmergencyEvent ───────────────────────────────────
        var emergencyEvent = await _db.EmergencyEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (emergencyEvent is null)
        {
            _logger.LogWarning("Postback 找不到事件：EventId={EventId}", eventId);
            await _db.SaveChangesAsync(ct);
            return;
        }

        // ── 驗證 feedback 在合法按鈕清單中 ───────────────────────
        var validButtons = ParseFlexButtons(emergencyEvent.FlexMsgItemsJson);
        if (!validButtons.Contains(feedback))
        {
            _logger.LogWarning(
                "Postback feedback 不在合法清單：EventId={EventId}, Feedback={Feedback}",
                eventId, feedback);
            await WriteAuditLogAsync(eventId, userId,
                $"W5003: feedback='{feedback}' 不在合法按鈕清單", ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        // ── 驗證使用者存在且非 observer ───────────────────────────
        var responder = await _db.EventResponders
            .FirstOrDefaultAsync(r =>
                r.EventId == eventId &&
                r.Account == userId, ct);

        if (responder is null)
        {
            _logger.LogWarning(
                "Postback 使用者不在名單：EventId={EventId}, UserId={UserId}", eventId, userId);
            await WriteAuditLogAsync(eventId, userId, "W5003: 使用者不在名單", ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (string.Equals(responder.Role, "observer", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Postback observer 不應回覆：EventId={EventId}, UserId={UserId}", eventId, userId);
            await WriteAuditLogAsync(eventId, userId, "W5004: observer 角色回傳 Postback", ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        // ── 更新 ReplyStatus（僅 Pending 狀態才更新）─────────────
        if (responder.ReplyStatus == "Pending")
        {
            responder.ReplyStatus  = feedback;
            responder.ReplyChannel = "Flex";
            responder.UpdatedAt    = DateTime.UtcNow;
        }
        else
        {
            _logger.LogInformation(
                "Postback 已有回覆，跳過更新：EventId={EventId}, UserId={UserId}, " +
                "ExistingStatus={Status}", eventId, userId, responder.ReplyStatus);
        }

        // ── WillArrive → INSERT Outbox: InviteTeamMember + InviteChatMember ──
        var intent = InferIntent(feedback);
        if (intent == ButtonIntent.WillArrive &&
            emergencyEvent.TeamPlusBigTeamSn.HasValue)
        {
            var commanderAccount = await _db.EventResponders
                .Where(r => r.EventId == eventId && r.Role == "commander")
                .Select(r => r.Account)
                .FirstOrDefaultAsync(ct)
                ?? responder.Account;

            _db.OutboxMessages.Add(new OutboxMessage
            {
                EventId     = eventId,
                MessageType = OutboxMessageType.InviteTeamMember,
                PayloadJson = JsonSerializer.Serialize(new InviteMemberOutboxPayload(
                    EventId:         eventId,
                    MemberAccount:   userId,
                    OperatorAccount: commanderAccount,
                    TargetSn:        (long)emergencyEvent.TeamPlusBigTeamSn.Value)),
                Status    = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            var chatSn = await _db.EventGroups
                .Where(g => g.EventId == eventId &&
                            g.ChatGp  == responder.ChatGp &&
                            g.TeamPlusChatSn.HasValue)
                .Select(g => g.TeamPlusChatSn)
                .FirstOrDefaultAsync(ct);

            if (chatSn.HasValue)
            {
                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventId     = eventId,
                    MessageType = OutboxMessageType.InviteChatMember,
                    PayloadJson = JsonSerializer.Serialize(new InviteMemberOutboxPayload(
                        EventId:         eventId,
                        MemberAccount:   userId,
                        OperatorAccount: commanderAccount,
                        TargetSn:        (long)chatSn.Value,
                        ChatGp:          responder.ChatGp)),
                    Status    = OutboxMessageStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // ── INSERT Outbox: UpdateFlexFooter ───────────────────────
        if (responder.FlexMessageSn.HasValue)
        {
            var (footerText, fontColor) = FooterTextHelper.GetFooter(feedback);
            _db.OutboxMessages.Add(new OutboxMessage
            {
                EventId     = eventId,
                MessageType = OutboxMessageType.UpdateFlexFooter,
                PayloadJson = JsonSerializer.Serialize(new UpdateFlexFooterOutboxPayload(
                    EventId:    eventId,
                    EventType:  emergencyEvent.EventType,
                    MessageSn:  responder.FlexMessageSn.Value,
                    Recipient:  userId,
                    FooterText: footerText,
                    FontColor:  fontColor)),
                Status    = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ── UPDATE WebhookInbox.ProcessedAt ───────────────────────
        inbox.ProcessedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // ── SignalR 推播（SaveChanges 後）────────────────────────
        try
        {
            await _notifier.NotifyStatsChangedAsync(eventId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR 推播失敗：EventId={EventId}", eventId);
        }

        _logger.LogInformation(
            "Postback 處理完成：EventId={EventId}, UserId={UserId}, " +
            "Feedback={Feedback}, Intent={Intent}",
            eventId, userId, feedback, intent);
    }

    private static string ComputeIdempotencyKey(WebhookEvent ev)
    {
        var raw = $"{ev.Timestamp}|{ev.Source.UserId}|{ev.Postback?.Data}";
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLower();
    }

    private static HashSet<string> ParseFlexButtons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
            return new HashSet<string>(list, StringComparer.Ordinal);
        }
        catch { return []; }
    }

    private static ButtonIntent InferIntent(string buttonText) =>
        buttonText.Trim() == "無法返回院區"
            ? ButtonIntent.CannotArrive
            : ButtonIntent.WillArrive;

    private async Task WriteAuditLogAsync(
        string eventId, string actor, string detail, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Category  = "WEBHOOK_IN",
            EventId   = eventId,
            Actor     = actor,
            Action    = "PostbackWebhook",
            Detail    = detail,
            CreatedAt = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private enum ButtonIntent { WillArrive, CannotArrive }
}
