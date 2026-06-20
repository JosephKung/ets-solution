using Ets.Domain.Enums;

namespace Ets.Domain.Entities;

/// <summary>
/// 臨時支援成員申請記錄。
/// 掃碼報到時若帳號不在正式名單，進入此申請流程，由現場組長審核。
/// 對應資料表：AdHocRequests。
/// </summary>
public class AdHocRequest
{
    /// <summary>申請識別碼（自動遞增）</summary>
    public long RequestId { get; set; }

    /// <summary>所屬事件識別碼</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>申請加入之 team+ 帳號</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>申請狀態</summary>
    public AdHocRequestStatus Status { get; set; } = AdHocRequestStatus.Pending;

    /// <summary>核准者帳號（現場組長）</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>核准後指派之分組交談室</summary>
    public string? AssignedChatGp { get; set; }

    /// <summary>申請時間（UTC）</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>審核決定時間（UTC）</summary>
    public DateTime? DecidedAt { get; set; }

    // ── 導航屬性 ──────────────────────────────────────────────────────────

    /// <summary>所屬事件</summary>
    public EmergencyEvent Event { get; set; } = null!;
}
