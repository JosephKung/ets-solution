// src/Ets.Infrastructure/Outbox/Handlers/SendObserverFlexOutboxHandler.cs
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
/// SendObserverFlex Outbox Handler（WBS 1.3.9）
///
/// 消費流程（§6.4.2）：
///   1. 反序列化 Payload
///   2. 冪等性：所有 ObserverAccounts 的 FlexMessageSn 皆已有值 → 跳過
///   3. 從 DB 載入 EmergencyEvent
///   4. 呼叫 IFlexMessageBuilder.BuildContentsWithoutButtons 組裝無按鈕 Flex
///   5. 呼叫 ITeamPlusChannelClient.BroadcastFlexMessageAsync
///   6. 成功 → UPDATE EventResponders.FlexMessageSn（observer 亦記錄 MessageSN，供已讀查詢）
///
/// 與 SendFlexMessageOutboxHandler（1.3.8）的差異：
/// - 使用 BuildContentsWithoutButtons（無 footer 區段）
/// - Payload 使用 ObserverAccounts（而非 RecipientAccounts）
/// - MessageType = SendFlexMessage（共用同一 enum 值，由 Payload 類型區分）
///   注意：若未來需要區分，可新增 SendObserverFlexMessage enum 值
/// </summary>
public sealed class SendObserverFlexOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusChannelClient _channelClient;
    private readonly IFlexMessageBuilder _flexBuilder;
    private readonly AppDbContext _db;
    private readonly ILogger<SendObserverFlexOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 使用獨立的 enum 值以便 OutboxDispatcherWorker 正確路由
    /// 需在 OutboxMessageType 補上 SendObserverFlexMessage = 11
    /// </summary>
    public OutboxMessageType MessageType => OutboxMessageType.SendObserverFlexMessage;

    public SendObserverFlexOutboxHandler(
        ITeamPlusChannelClient channelClient,
        IFlexMessageBuilder flexBuilder,
        AppDbContext db,
        ILogger<SendObserverFlexOutboxHandler> logger)
    {
        _channelClient = channelClient;
        _flexBuilder   = flexBuilder;
        _db            = db;
        _logger        = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SendObserverFlexOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（SendObserverFlex）");

        _logger.LogInformation(
            "SendObserverFlex 開始：OutboxId={OutboxId}, EventId={EventId}, Observers={Count}",
            outboxId, payload.EventId, payload.ObserverAccounts.Count);

        if (payload.ObserverAccounts.Count == 0)
        {
            _logger.LogInformation(
                "SendObserverFlex 跳過：EventId={EventId} 無 observer", payload.EventId);
            return;
        }

        // ── 冪等性：所有 observer 已有 FlexMessageSn → 跳過 ──────
        var alreadySentCount = await _db.EventResponders
            .CountAsync(r =>
                r.EventId == payload.EventId &&
                payload.ObserverAccounts.Contains(r.Account) &&
                r.FlexMessageSn.HasValue, ct);

        if (alreadySentCount == payload.ObserverAccounts.Count)
        {
            _logger.LogWarning(
                "SendObserverFlex 冪等跳過：EventId={EventId} 所有 observer 已有 FlexMessageSn",
                payload.EventId);
            return;
        }

        // ── 載入 EmergencyEvent ───────────────────────────────────
        var ev = await _db.EmergencyEvents
            .FirstOrDefaultAsync(e => e.EventId == payload.EventId, ct)
            ?? throw new InvalidOperationException(
                $"EmergencyEvents 找不到 EventId={payload.EventId}");

        // ── 組裝無按鈕 Flex contents（§6.4.2）────────────────────
        var flexContents = _flexBuilder.BuildContentsWithoutButtons(ev);

        // ── 呼叫 team+ broadcastMessageByLoginNameList ────────────
        var request = new BroadcastFlexMessageRequest(
            EventType:     payload.EventType,
            RecipientList: payload.ObserverAccounts,
            FlexContents:  flexContents);

        var result = await _channelClient.BroadcastFlexMessageAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"SendObserverFlex 廣播失敗：EventId={payload.EventId}");
        }

        _logger.LogInformation(
            "SendObserverFlex 成功：EventId={EventId}, MessageSn={MessageSn}",
            payload.EventId, result.MessageSN);

        // ── 回填 observer 的 FlexMessageSn ───────────────────────
        var observers = await _db.EventResponders
            .Where(r =>
                r.EventId == payload.EventId &&
                payload.ObserverAccounts.Contains(r.Account))
            .ToListAsync(ct);

        foreach (var observer in observers)
        {
            observer.FlexMessageSn = result.MessageSN;
            observer.UpdatedAt     = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SendObserverFlex 完成：EventId={EventId}, MessageSn={MessageSn}, Updated={Count}",
            payload.EventId, result.MessageSN, observers.Count);
    }
}
