// src/Ets.Application/UseCases/TeamPlus/SendFlexMessageOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// SendFlexMessage Outbox 任務 Payload
/// 由 M2 EventTriggerHandler 寫入，由 1.3.8 SendFlexMessageOutboxHandler 消費
/// </summary>
public sealed record SendFlexMessageOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>事件類型 a~e（選取對應服務頻道 AccessToken）</summary>
    string EventType,

    /// <summary>
    /// 收件人帳號清單（排除 observer）
    /// observer 使用另一個 SendFlexObserverMessage Payload（1.3.9）
    /// </summary>
    IReadOnlyList<string> RecipientAccounts);
