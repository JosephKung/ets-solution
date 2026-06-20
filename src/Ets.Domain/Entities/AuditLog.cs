namespace Ets.Domain.Entities;

/// <summary>
/// 稽核日誌表。
/// 記錄所有 API 進出、Webhook 接收、狀態變更等可稽核事件。
/// 對應資料表：AuditLogs。
/// </summary>
public class AuditLog
{
    /// <summary>稽核記錄識別碼（自動遞增）</summary>
    public long AuditId { get; set; }

    /// <summary>
    /// 稽核類別。
    /// 固定值：API_IN / API_OUT / WEBHOOK_IN / STATUS_CHANGE
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>關聯事件識別碼（可為 null，非事件相關操作）</summary>
    public string? EventId { get; set; }

    /// <summary>操作者（系統名稱或使用者帳號）</summary>
    public string? Actor { get; set; }

    /// <summary>操作行為描述（如 "HIS_EVENT_TRIGGER"、"FLEX_REPLY"）</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>操作細節（JSON 格式，注意：電話號碼等個資需遮罩）</summary>
    public string? Detail { get; set; }

    /// <summary>HTTP 回應狀態碼（API 呼叫相關）</summary>
    public int? HttpStatus { get; set; }

    /// <summary>操作耗時（毫秒）</summary>
    public int? DurationMs { get; set; }

    /// <summary>記錄時間（UTC）</summary>
    public DateTime CreatedAt { get; set; }
}
