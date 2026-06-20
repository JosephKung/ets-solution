using Ets.Domain.Enums;

namespace Ets.Domain.Entities;

/// <summary>
/// Outbox Pattern 派送箱。
/// 所有對外整合呼叫（team+ API）先寫入此表，由 OutboxDispatcherWorker 非同步消費。
/// 確保「DB 事務」與「外部 API 呼叫」的最終一致性。
/// 對應資料表：OutboxMessages。
/// </summary>
public class OutboxMessage
{
    /// <summary>Outbox 訊息識別碼（自動遞增）</summary>
    public long OutboxId { get; set; }

    /// <summary>所屬事件識別碼</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>訊息類型（決定 OutboxDispatcherWorker 呼叫哪個 handler）</summary>
    public OutboxMessageType MessageType { get; set; }

    /// <summary>任務 Payload（JSON 序列化後的操作參數）</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>派送狀態</summary>
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    /// <summary>已重試次數</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>最後一次錯誤訊息</summary>
    public string? LastError { get; set; }

    /// <summary>下次重試時間（指數退避計算）</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>成功派送時間</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>建立時間（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    // ── 導航屬性 ──────────────────────────────────────────────────────────

    /// <summary>所屬事件</summary>
    public EmergencyEvent Event { get; set; } = null!;
}
