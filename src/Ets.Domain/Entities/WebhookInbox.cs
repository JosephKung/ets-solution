namespace Ets.Domain.Entities;

/// <summary>
/// Webhook 接收紀錄表（冪等性保護）。
/// team+ 與語音 API 可能因網路抖動重送 Webhook，以唯一 (Source, ExternalMessageID) 去重。
/// 對應資料表：WebhookInbox。
/// </summary>
public class WebhookInbox
{
    /// <summary>收件匣識別碼（自動遞增）</summary>
    public long InboxId { get; set; }

    /// <summary>
    /// 來源系統識別碼。
    /// 固定值：teamplus / voicebot
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 外部系統提供之唯一訊息 ID（冪等鍵）。
    /// team+ Postback：由 (timestamp + userId + data) 三元組 SHA-256 哈希產生。
    /// </summary>
    public string ExternalMessageId { get; set; } = string.Empty;

    /// <summary>關聯事件識別碼（可為 null，處理前尚未解析時）</summary>
    public string? EventId { get; set; }

    /// <summary>觸發帳號</summary>
    public string? Account { get; set; }

    /// <summary>原始 Payload（完整 JSON 字串，供稽核與重送使用）</summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>簽章驗證結果</summary>
    public bool SignatureValid { get; set; }

    /// <summary>接收時間（UTC）</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>處理完成時間（UTC，null 表示尚未處理）</summary>
    public DateTime? ProcessedAt { get; set; }
}
