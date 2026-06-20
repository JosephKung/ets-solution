// src/Ets.Infrastructure/Webhooks/VoiceCallbackHandler.cs
using System.Text.Json;
using Ets.Application.UseCases.Voice;
using Ets.Domain.Entities;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Webhooks;

/// <summary>
/// Voice Callback Webhook 處理器（WBS 1.4.5 + 1.4.6 + 1.4.7 + 1.4.8）
///
/// 8 階段處理流程（§9.5.4）：
///   QUEUED / DIALING / RINGING / ANSWERED → 更新 LastVoiceStatus
///   REJECTED → 終態，更新 LastVoiceStatus，不再有後續 webhook
///   PLAYING / PLAY_DONE → 更新 LastVoiceStatus
///   COMPLETED → 終態，更新 LastVoiceStatus + ReplyStatus 自動 transition
///
/// 鐵律（§9.5）：
///   ① 以最後一筆為準（直接覆寫 LastVoiceStatus）
///   ② REJECTED 為絕對終態
///   ③ 不會收到重複 status（冪等鍵 = (external_call_id, status)）
///   ④ webhook 失敗 voice API 自動 retry 3 次 → 必須冪等
///   ⑤ timeout 上限 10 秒 → 即使失敗也回 200
///   ⑥ dashboard 只需最後 status → 不需通話總耗時
/// </summary>
public sealed class VoiceCallbackHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<VoiceCallbackHandler> _logger;

    /// <summary>終態集合（收到後不再有任何 webhook）</summary>
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "REJECTED", "COMPLETED" };

    public VoiceCallbackHandler(
        AppDbContext db,
        ILogger<VoiceCallbackHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task HandleAsync(VoiceCallbackBody body, CancellationToken ct)
    {
        var externalCallId = body.ExternalCallId;
        var status         = body.Status?.ToUpperInvariant() ?? string.Empty;

        _logger.LogInformation(
            "VoiceCallback 收到：ExternalCallId={CallId}, Status={Status}",
            externalCallId, status);

        // ── 1.4.6 冪等性：查 WebhookInbox（(external_call_id, status) 唯一鍵）──
        var idempotencyKey = $"{externalCallId}|{status}";
        var exists = await _db.WebhookInboxes
            .AnyAsync(w =>
                w.Source            == "voicebot" &&
                w.ExternalMessageId == idempotencyKey, ct);

        if (exists)
        {
            _logger.LogInformation(
                "VoiceCallback 冪等跳過：CallId={CallId}, Status={Status}",
                externalCallId, status);
            return;
        }

        // INSERT WebhookInbox（冪等保護）
        var inbox = new WebhookInbox
        {
            Source            = "voicebot",
            ExternalMessageId = idempotencyKey,
            RawPayload        = JsonSerializer.Serialize(body),
            SignatureValid    = true,  // voice API 用 X-ETS-API-Key（Filter 已驗證）
            ReceivedAt        = DateTime.UtcNow
        };
        _db.WebhookInboxes.Add(inbox);

        // ── 找出對應 EventResponder ───────────────────────────────
        var responder = await _db.EventResponders
            .FirstOrDefaultAsync(r => r.LastExternalCallId == externalCallId, ct);

        if (responder is null)
        {
            _logger.LogWarning(
                "VoiceCallback 找不到對應 Responder：CallId={CallId}", externalCallId);
            // 仍寫入 WebhookInbox 並回 200（防止 voice API 重送）
            await _db.SaveChangesAsync(ct);
            return;
        }

        inbox.EventId = responder.EventId;
        inbox.Account = responder.Account;

        // ── 1.4.5 更新 LastVoiceStatus（直接覆寫，以最後一筆為準）─
        responder.LastVoiceStatus   = status;
        responder.LastVoiceStatusAt = DateTime.UtcNow;
        responder.UpdatedAt         = DateTime.UtcNow;

        // ── 1.4.7 終態邏輯 ───────────────────────────────────────
        if (TerminalStatuses.Contains(status))
        {
            _logger.LogInformation(
                "VoiceCallback 終態：CallId={CallId}, Status={Status}, Account={Account}",
                externalCallId, status, responder.Account);

            // ── 1.4.8 COMPLETED → ReplyStatus 自動 transition ────
            if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCompletedAsync(responder, externalCallId, ct);
            }
            // REJECTED → 已在 LastVoiceStatus 記錄，不改 ReplyStatus
        }

        inbox.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "VoiceCallback 處理完成：CallId={CallId}, Status={Status}, " +
            "Account={Account}, EventId={EventId}",
            externalCallId, status, responder.Account, responder.EventId);
    }

    // ─── 1.4.8 COMPLETED 終態處理 ────────────────────────────────

    /// <summary>
    /// COMPLETED 表示語音已成功播放完畢
    /// 若 ReplyStatus 仍為 Pending → 更新為 VoiceConfirmed（§7.3）
    /// 同時排入 UpdateFlexFooter Outbox（替換 footer 為「語音已送達！請開 team+ 回覆」）
    /// </summary>
    private async Task HandleCompletedAsync(
        EventResponder responder,
        string externalCallId,
        CancellationToken ct)
    {
        if (responder.ReplyStatus == "Pending")
        {
            responder.ReplyStatus  = "VoiceConfirmed";
            responder.ReplyChannel = "Voice";

            _logger.LogInformation(
                "VoiceCallback COMPLETED → ReplyStatus=VoiceConfirmed：" +
                "Account={Account}, EventId={EventId}",
                responder.Account, responder.EventId);

            // 排入 UpdateFlexFooter Outbox（若有 FlexMessageSn）
            if (responder.FlexMessageSn.HasValue)
            {
                var ev = await _db.EmergencyEvents
                    .Select(e => new { e.EventId, e.EventType })
                    .FirstOrDefaultAsync(e => e.EventId == responder.EventId, ct);

                if (ev is not null)
                {
                    var (footerText, fontColor) =
                        Application.UseCases.TeamPlus.FooterTextHelper.GetFooter("VoiceConfirmed");

                    _db.OutboxMessages.Add(new OutboxMessage
                    {
                        EventId     = responder.EventId,
                        MessageType = Domain.Enums.OutboxMessageType.UpdateFlexFooter,
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            EventId    = responder.EventId,
                            EventType  = ev.EventType,
                            MessageSn  = responder.FlexMessageSn.Value,
                            Recipient  = responder.Account,
                            FooterText = footerText,
                            FontColor  = fontColor
                        }),
                        Status    = Domain.Enums.OutboxMessageStatus.Pending,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }
    }
}
