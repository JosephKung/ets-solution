// src/Ets.Application/Interfaces/IOutboxHandler.cs
using Ets.Domain.Enums;

namespace Ets.Application.Interfaces;

/// <summary>
/// Outbox 訊息處理器介面
/// 每種 OutboxMessageType 對應一個實作（Strategy Pattern）
/// OutboxDispatcherWorker 依 MessageType 動態解析並呼叫對應 Handler
/// </summary>
public interface IOutboxHandler
{
    /// <summary>本 Handler 負責的 MessageType（對應 OutboxMessage.MessageType 欄位）</summary>
    OutboxMessageType MessageType { get; }

    /// <summary>
    /// 處理 Outbox 訊息
    /// </summary>
    /// <param name="outboxId">OutboxMessages.OutboxId（用於日誌追蹤）</param>
    /// <param name="payloadJson">OutboxMessages.PayloadJson（需自行反序列化）</param>
    /// <param name="ct">CancellationToken</param>
    Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct);
}
