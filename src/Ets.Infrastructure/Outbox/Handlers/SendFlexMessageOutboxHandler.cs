// src/Ets.Infrastructure/Outbox/Handlers/SendFlexMessageOutboxHandler.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Outbox.Handlers;

/// <summary>
/// SendFlexMessage Outbox Handler（WBS 1.3.8）
///
/// 消費流程（§6.4）：
///   1. 反序列化 Payload
///   2. 冪等性：所有 RecipientAccounts 的 FlexMessageSn 皆已有值 → 跳過
///   3. 從 DB 載入 EmergencyEvent（含 FlexMsgItemsJson）
///   4. 呼叫 IFlexMessageBuilder.BuildContentsWithButtons 組裝 Flex contents
///   5. 呼叫 ITeamPlusChannelClient.BroadcastFlexMessageAsync
///   6. 成功 → UPDATE EventResponders.FlexMessageSn（所有收件人同一個 MessageSN）
/// </summary>
public sealed class SendFlexMessageOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusChannelClient _channelClient;
    private readonly IFlexMessageBuilder _flexBuilder;
    private readonly AppDbContext _db;
    private readonly ILogger<SendFlexMessageOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.SendFlexMessage;

    public SendFlexMessageOutboxHandler(
        ITeamPlusChannelClient channelClient,
        IFlexMessageBuilder flexBuilder,
        AppDbContext db,
        ILogger<SendFlexMessageOutboxHandler> logger)
    {
        _channelClient = channelClient;
        _flexBuilder   = flexBuilder;
        _db            = db;
        _logger        = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SendFlexMessageOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（SendFlexMessage）");

        _logger.LogInformation(
            "SendFlexMessage 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "Recipients={Count}",
            outboxId, payload.EventId, payload.RecipientAccounts.Count);

        // ── 冪等性：已全部發送過 → 跳過 ──────────────────────────
        var alreadySentCount = await _db.EventResponders
            .CountAsync(r =>
                r.EventId == payload.EventId &&
                payload.RecipientAccounts.Contains(r.Account) &&
                r.FlexMessageSn.HasValue, ct);

        if (alreadySentCount == payload.RecipientAccounts.Count)
        {
            _logger.LogWarning(
                "SendFlexMessage 冪等跳過：EventId={EventId} 所有收件人已有 FlexMessageSn",
                payload.EventId);
            return;
        }

        // ── 載入 EmergencyEvent ───────────────────────────────────
        var ev = await _db.EmergencyEvents
            .FirstOrDefaultAsync(e => e.EventId == payload.EventId, ct)
            ?? throw new InvalidOperationException(
                $"EmergencyEvents 找不到 EventId={payload.EventId}");

        // ── 組裝 Flex contents ────────────────────────────────────
        var flexContents = _flexBuilder.BuildContentsWithButtons(ev);

        // ── 呼叫 team+ broadcastMessageByLoginNameList ────────────
        var request = new BroadcastFlexMessageRequest(
            EventType:     payload.EventType,
            RecipientList: payload.RecipientAccounts,
            FlexContents:  flexContents);

        var result = await _channelClient.BroadcastFlexMessageAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"broadcastMessageByLoginNameList 失敗：EventId={payload.EventId}");
        }

        _logger.LogInformation(
            "SendFlexMessage 成功：EventId={EventId}, MessageSn={MessageSn}",
            payload.EventId, result.MessageSN);

        // ── 回填所有收件人的 FlexMessageSn ───────────────────────
        // 同一次廣播所有人共用同一個 MessageSN（§6.4 規格）
        var responders = await _db.EventResponders
            .Where(r =>
                r.EventId == payload.EventId &&
                payload.RecipientAccounts.Contains(r.Account))
            .ToListAsync(ct);

        foreach (var responder in responders)
        {
            responder.FlexMessageSn = result.MessageSN;
            responder.UpdatedAt     = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SendFlexMessage 完成：EventId={EventId}, MessageSn={MessageSn}, " +
            "Updated={Count} responders",
            payload.EventId, result.MessageSN, responders.Count);
    }
}
