namespace Ets.Application.Interfaces;

/// <summary>
/// Dashboard SignalR 推播介面。
/// 實作於 M6（1.6.8 SignalR Hub），M5 前使用 Null 實作。
/// </summary>
public interface IDashboardNotifier
{
    /// <summary>通知指定事件之 Dashboard 統計數據已變動（觸發前端重整）</summary>
    Task NotifyStatsChangedAsync(string eventId, CancellationToken ct = default);

    /// <summary>通知 Dashboard：有人完成現場報到（CheckInRegistered，§8.5 Step 10）</summary>
    Task NotifyCheckInAsync(
        string eventId,
        string account,
        DateTime checkInAt,
        CancellationToken ct = default);
}
