namespace Ets.Domain.Enums;

/// <summary>
/// 緊急事件狀態。
/// 對應 EmergencyEvents.Status 欄位（TINYINT）。
/// </summary>
public enum EventStatus : byte
{
    /// <summary>處理中（初始狀態）</summary>
    Processing = 0,

    /// <summary>已結案</summary>
    Closed = 1,

    /// <summary>已取消</summary>
    Cancelled = 2
}
