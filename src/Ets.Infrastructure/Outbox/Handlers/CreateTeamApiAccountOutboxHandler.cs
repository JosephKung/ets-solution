// src/Ets.Infrastructure/Outbox/Handlers/CreateTeamApiAccountOutboxHandler.cs
using System.Text.Json;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Outbox.Handlers;

/// <summary>
/// CreateTeamAPIAccount Outbox Handler（WBS 1.3.10 / 1.3.11）
///
/// 消費流程（§6.6.3）：
///   1. 反序列化 Payload
///   2. 冪等性：TeamPlusVirtualAccount 已有值 → 跳過
///   3. 呼叫 ITeamPlusSystemClient.CreateTeamApiAccountAsync
///   4. 成功 → 回填 EmergencyEvents.TeamPlusVirtualAccount（明文帳號）
///             ApiKey 以 AES-256-GCM 加密後寫入 TeamPlusVirtualAccountApiKey（1.3.11）
///   5. INSERT 後續 Outbox：PostVirtualMsg（§6.6.4）
///      PostVirtualMsg Payload 不再帶明文 ApiKey，改由 Handler 從 DB 解密取得
/// </summary>
public sealed class CreateTeamApiAccountOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly IVirtualAccountKeyEncryptor _encryptor;
    private readonly AppDbContext _db;
    private readonly ILogger<CreateTeamApiAccountOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.CreateTeamAPIAccount;

    public CreateTeamApiAccountOutboxHandler(
        ITeamPlusSystemClient systemClient,
        IVirtualAccountKeyEncryptor encryptor,
        AppDbContext db,
        ILogger<CreateTeamApiAccountOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _encryptor    = encryptor;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CreateTeamApiAccountOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（CreateTeamAPIAccount）");

        _logger.LogInformation(
            "CreateTeamAPIAccount 開始：OutboxId={OutboxId}, EventId={EventId}, TeamSn={TeamSn}",
            outboxId, payload.EventId, payload.TeamSn);

        // ── 冪等性：VirtualAccount 已有值 → 跳過 ─────────────────
        var ev = await _db.EmergencyEvents
            .FirstOrDefaultAsync(e => e.EventId == payload.EventId, ct)
            ?? throw new InvalidOperationException(
                $"EmergencyEvents 找不到 EventId={payload.EventId}");

        if (!string.IsNullOrEmpty(ev.TeamPlusVirtualAccount))
        {
            _logger.LogWarning(
                "CreateTeamAPIAccount 冪等跳過：EventId={EventId} 虛擬帳號已存在",
                payload.EventId);
            return;
        }

        // ── 呼叫 team+ createTeamAPIAccount ──────────────────────
        var result = await _systemClient.CreateTeamApiAccountAsync(
            teamSn:       payload.TeamSn,
            ownerAccount: payload.OwnerAccount,
            accountName:  "緊急應變智能通報",
            ct:           ct);

        if (!result.IsSuccess || string.IsNullOrEmpty(result.ApiAccount))
        {
            throw new InvalidOperationException(
                $"createTeamAPIAccount 失敗：EventId={payload.EventId}, " +
                $"ErrorCode={result.ErrorCode}, Description={result.Description}");
        }

        _logger.LogInformation(
            "createTeamAPIAccount 成功：EventId={EventId}, ApiAccount={ApiAccount}",
            payload.EventId, result.ApiAccount);

        // ── 回填虛擬帳號（1.3.10）+ AES 加密 ApiKey（1.3.11）────
        ev.TeamPlusVirtualAccount    = result.ApiAccount;
        ev.TeamPlusVirtualAccountApiKey = _encryptor.Encrypt(result.ApiKey);

        // ── 組裝事件貼文內容（Figma A-3 格式）───────────────────
        var textContent = BuildPostContent(ev);
        var subject     = $"{ev.EventType.ToUpper()} {ev.EventSummary} 事件記錄";

        // ── 插入後續 Outbox：PostVirtualMsg ──────────────────────
        // ApiKey 已加密寫入 DB，PostVirtualMsgHandler 從 DB 解密取得，不再放入 Payload
        var postPayload = JsonSerializer.Serialize(new PostVirtualMsgOutboxPayload(
            EventId:     payload.EventId,
            TeamSn:      payload.TeamSn,
            TextContent: textContent,
            Subject:     subject));

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventId     = payload.EventId,
            MessageType = OutboxMessageType.PostVirtualMsg,
            PayloadJson = postPayload,
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CreateTeamAPIAccount 完成：EventId={EventId}, ApiKey 已加密寫入 DB，已排入 PostVirtualMsg Outbox",
            payload.EventId);
    }

    /// <summary>依 Figma A-3 格式組裝事件貼文內容</summary>
    private static string BuildPostContent(EmergencyEvent ev)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("緊急應變智能通報");
        sb.AppendLine(ev.EventTime.ToString("yyyy/MM/dd HH:mm"));
        sb.AppendLine();
        sb.AppendLine($"事件類型：{ev.EventType} {ev.EventSummary}");

        if (!string.IsNullOrWhiteSpace(ev.EventDescription))
            sb.AppendLine($"通報內容：{ev.EventDescription}");

        if (!string.IsNullOrWhiteSpace(ev.AudioContent))
            sb.AppendLine($"電話語音內容：{ev.AudioContent}");

        return sb.ToString().TrimEnd();
    }
}
