namespace Ets.Domain.Enums;

/// <summary>
/// Outbox 訊息派送狀態。
/// 對應 OutboxMessages.Status 欄位（TINYINT）。
/// </summary>
public enum OutboxMessageStatus : byte
{
    /// <summary>待派送（初始狀態）</summary>
    Pending = 0,

    /// <summary>派送中（Worker 正在處理）</summary>
    Processing = 1,

    /// <summary>已完成</summary>
    Done = 2,

    /// <summary>失敗（尚有重試次數）</summary>
    Failed = 3,

    /// <summary>死信佇列（已超過最大重試次數）</summary>
    DeadLetterQueue = 4
}
