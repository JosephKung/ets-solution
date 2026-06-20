// src/Ets.Infrastructure/BackgroundServices/ReadStatusPollingWorker.cs
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// 已讀狀態輪詢 Worker（WBS 1.3.12）
///
/// 每 30 秒掃描一次所有「進行中」事件（Status=0），
/// 對已發送 Flex Message（FlexMessageSn 有值）的收件人批次查詢已讀數，
/// 若 ReadCount 有變化則：
///   1. UPDATE EmergencyEvents.LastReadCount + LastReadCountFetchAt
///   2. 觸發 IDashboardNotifier.NotifyStatsChangedAsync（SignalR 推播）
///
/// 規格書參照：§6.8 / §10.2（Dashboard 已讀統計）
/// </summary>
public sealed class ReadStatusPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReadStatusPollingWorker> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public ReadStatusPollingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReadStatusPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ReadStatusPollingWorker 啟動，輪詢間隔 {Interval}s",
            PollInterval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollAllActiveEventsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ReadStatusPollingWorker 批次執行發生例外");
            }

            await Task.Delay(PollInterval, ct);
        }

        _logger.LogInformation("ReadStatusPollingWorker 停止");
    }

    private async Task PollAllActiveEventsAsync(CancellationToken ct)
    {
        using var scope      = _scopeFactory.CreateScope();
        var db               = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channelClient    = scope.ServiceProvider.GetRequiredService<ITeamPlusChannelClient>();
        var dashNotifier     = scope.ServiceProvider.GetRequiredService<IDashboardNotifier>();

        // 查詢所有進行中事件（Status=0）且已有 Flex MessageSN 可查詢的
        // 使用各事件第一筆有 FlexMessageSn 的收件人取得 MessageSn
        var activeEvents = await db.EmergencyEvents
            .Where(e => e.Status == Domain.Enums.EventStatus.Processing)
            .Select(e => new
            {
                e.EventId,
                e.EventType,
                e.LastReadCount,
                // 取該事件任一收件人的 FlexMessageSn（同一事件廣播同一 MessageSN）
                MessageSn = db.EventResponders
                    .Where(r => r.EventId == e.EventId && r.FlexMessageSn.HasValue)
                    .Select(r => r.FlexMessageSn)
                    .FirstOrDefault()
            })
            .Where(e => e.MessageSn.HasValue)
            .ToListAsync(ct);

        if (activeEvents.Count == 0) return;

        _logger.LogDebug("ReadStatusPolling：掃描 {Count} 個進行中事件", activeEvents.Count);

        foreach (var ev in activeEvents)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await PollSingleEventAsync(
                    db, channelClient, dashNotifier,
                    ev.EventId, ev.EventType,
                    ev.MessageSn!.Value, ev.LastReadCount,
                    ct);
            }
            catch (Exception ex)
            {
                // 單一事件失敗不影響其他事件
                _logger.LogWarning(ex,
                    "ReadStatusPolling 單一事件失敗：EventId={EventId}", ev.EventId);
            }
        }
    }

    private async Task PollSingleEventAsync(
        AppDbContext db,
        ITeamPlusChannelClient channelClient,
        IDashboardNotifier dashNotifier,
        string eventId,
        string eventType,
        int messageSn,
        int? lastReadCount,
        CancellationToken ct)
    {
        // 呼叫 team+ getMsgReadStatus（§6.8）
        var result = await channelClient.GetMsgReadStatusAsync(
            new GetMsgReadStatusRequest(
                EventType: eventType,
                MessageSN: messageSn),
            ct);

        // ReadCount 無變化 → 不更新 DB，不推播（避免無謂 SignalR 廣播）
        if (result.ReadCount == lastReadCount)
        {
            _logger.LogDebug(
                "ReadStatusPolling：EventId={EventId} ReadCount={ReadCount} 無變化，跳過",
                eventId, result.ReadCount);
            return;
        }

        _logger.LogInformation(
            "ReadStatusPolling：EventId={EventId} ReadCount {Old} → {New}",
            eventId, lastReadCount ?? 0, result.ReadCount);

        // UPDATE EmergencyEvents.LastReadCount + LastReadCountFetchAt
        var ev = await db.EmergencyEvents.FindAsync(new object[] { eventId }, ct);
        if (ev is null) return;

        ev.LastReadCount        = result.ReadCount;
        ev.LastReadCountFetchAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // 觸發 SignalR 推播（§10.4.3）
        await dashNotifier.NotifyStatsChangedAsync(eventId, ct);
    }
}
