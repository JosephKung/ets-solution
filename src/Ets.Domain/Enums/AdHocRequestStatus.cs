namespace Ets.Domain.Enums;

/// <summary>
/// 臨時支援成員申請狀態。
/// 對應 AdHocRequests.Status 欄位。
/// </summary>
public enum AdHocRequestStatus
{
    /// <summary>待審核</summary>
    Pending,

    /// <summary>已核准</summary>
    Approved,

    /// <summary>已拒絕</summary>
    Rejected
}
