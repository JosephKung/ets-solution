// src/Ets.Infrastructure/Outbox/Handlers/UpdateFlexFooterOutboxHandler.cs
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
/// UpdateFlexFooter Outbox Handler（WBS 1.3.13）
///
/// 消費流程（§6.9）：
///   1. 反序列化 Payload
///   2. 呼叫 ITeamPlusChannelClient.UpdateFlexFooterAsync
///      將 footer 從「N 個按鈕」替換為「已送出！」紅字
///   3. 成功記錄 log（無需回填 DB，Footer 更新是單向操作）
///
/// 觸發時機（§7.2 Webhook 處理流程）：
///   使用者點擊 Flex Message 按鈕 → Postback Webhook Handler
///   → INSERT Outbox: UpdateFlexFooter（本 Handler）
/// </summary>
public sealed class UpdateFlexFooterOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusChannelClient _channelClient;
    private readonly AppDbContext _db;
    private readonly ILogger<UpdateFlexFooterOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.UpdateFlexFooter;

    public UpdateFlexFooterOutboxHandler(
        ITeamPlusChannelClient channelClient,
        AppDbContext db,
        ILogger<UpdateFlexFooterOutboxHandler> logger)
    {
        _channelClient = channelClient;
        _db            = db;
        _logger        = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UpdateFlexFooterOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（UpdateFlexFooter）");

        _logger.LogInformation(
            "UpdateFlexFooter 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "Recipient={Recipient}, MessageSn={MessageSn}",
            outboxId, payload.EventId, payload.Recipient, payload.MessageSn);

        // ── 冪等性說明 ────────────────────────────────────────────
        // updateFlexMessageFooter 本身是冪等的（重複呼叫只是再更新一次相同文字）
        // 依規格 §11.3：由觸發條件控制（僅 ReplyChannel 變動時觸發），不需額外 DB 防重

        var request = new UpdateFlexFooterRequest(
            EventType:  payload.EventType,
            MessageSN:  payload.MessageSn,
            Recipient:  payload.Recipient,
            FooterText: payload.FooterText,
            FontColor:  payload.FontColor);

        var result = await _channelClient.UpdateFlexFooterAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"updateFlexMessageFooter 失敗：EventId={payload.EventId}, " +
                $"Recipient={payload.Recipient}, ErrorCode={result.ErrorCode}");
        }

        _logger.LogInformation(
            "UpdateFlexFooter 完成：EventId={EventId}, Recipient={Recipient}, " +
            "FooterText={FooterText}",
            payload.EventId, payload.Recipient, payload.FooterText);
    }
}
