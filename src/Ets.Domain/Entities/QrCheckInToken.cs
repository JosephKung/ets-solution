namespace Ets.Domain.Entities;

/// <summary>
/// QR 報到一次性 Token（5 分鐘輪替機制）。
/// 每 300 秒自動輪替，舊 Token 保留 60 秒寬限期（Grace Period）。
/// 對應資料表：QrCheckInTokens。
/// </summary>
public class QrCheckInToken
{
    /// <summary>Token 識別碼（GUID，嵌入 QR Code 連結）</summary>
    public Guid TokenId { get; set; }

    /// <summary>所屬事件識別碼</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>64 字元隨機字串（嵌入 QR Code URL 中）</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 短簽章（驗證 Nonce 未被竄改）</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>簽發時間（UTC）</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>到期時間（UTC，預設 5 分鐘後）</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>被輪替時間（UTC）</summary>
    public DateTime? RotatedAt { get; set; }

    /// <summary>是否有效（輪替後設為 false）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 寬限期截止時間（UTC）。
    /// 輪替後 60 秒內，舊 Token 仍可成功報到（避免使用者恰好在輪替瞬間掃碼失敗）。
    /// </summary>
    public DateTime? GracePeriodEndAt { get; set; }

    // ── 導航屬性 ──────────────────────────────────────────────────────────

    /// <summary>所屬事件</summary>
    public EmergencyEvent Event { get; set; } = null!;
}
