using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.HealthChecks;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Ets.UnitTests.Infrastructure.HealthChecks;

/// <summary>
/// OutboxQueueDepthHealthCheck 單元測試。
/// 使用 EF Core InMemory Provider 模擬各種佇列深度情境。
/// </summary>
public class OutboxQueueDepthHealthCheckTests : IDisposable
{
    private readonly AppDbContext _db;

    public OutboxQueueDepthHealthCheckTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    [Fact(DisplayName = "Outbox 為空時應回 Healthy")]
    public async Task EmptyOutbox_ShouldReturn_Healthy()
    {
        // Arrange
        var check   = new OutboxQueueDepthHealthCheck(_db);
        var context = MakeContext();

        // Act
        var result = await check.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["outbox_pending"].Should().Be(0);
        result.Data["outbox_dlq"].Should().Be(0);
    }

    [Fact(DisplayName = "Pending 超過 100 應回 Degraded")]
    public async Task PendingAbove100_ShouldReturn_Degraded()
    {
        // Arrange — 塞入 101 筆 Pending
        await SeedOutboxAsync(OutboxMessageStatus.Pending, count: 101);
        var check = new OutboxQueueDepthHealthCheck(_db);

        // Act
        var result = await check.CheckHealthAsync(MakeContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded,
            because: "Pending > 100 應進入 Degraded，提醒維運人員注意");
        ((int)result.Data["outbox_pending"]).Should().BeGreaterThan(100);
    }

    [Fact(DisplayName = "Pending 超過 500 應回 Unhealthy")]
    public async Task PendingAbove500_ShouldReturn_Unhealthy()
    {
        // Arrange
        await SeedOutboxAsync(OutboxMessageStatus.Pending, count: 501);
        var check = new OutboxQueueDepthHealthCheck(_db);

        // Act
        var result = await check.CheckHealthAsync(MakeContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy,
            because: "Pending > 500 代表 OutboxDispatcherWorker 可能已停止運作");
    }

    [Fact(DisplayName = "有任何 DLQ 訊息應立即回 Unhealthy")]
    public async Task AnyDlq_ShouldReturn_Unhealthy()
    {
        // Arrange — 只有 1 筆 DLQ，Pending 為 0
        await SeedOutboxAsync(OutboxMessageStatus.DeadLetterQueue, count: 1);
        var check = new OutboxQueueDepthHealthCheck(_db);

        // Act
        var result = await check.CheckHealthAsync(MakeContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy,
            because: "DLQ > 0 代表有訊息永久無法派送至 team+，需立即人工介入");
        ((int)result.Data["outbox_dlq"]).Should().Be(1);
    }

    [Fact(DisplayName = "Done 狀態的訊息不應影響健康狀態")]
    public async Task DoneMessages_ShouldNot_AffectHealth()
    {
        // Arrange — 塞入大量已完成的訊息
        await SeedOutboxAsync(OutboxMessageStatus.Done, count: 1000);
        var check = new OutboxQueueDepthHealthCheck(_db);

        // Act
        var result = await check.CheckHealthAsync(MakeContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy,
            because: "已處理完成的訊息不應影響健康判斷");
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────

    private async Task SeedOutboxAsync(OutboxMessageStatus status, int count)
    {
        // 先建立一個 EmergencyEvent（FK 約束）
        var ev = new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventTime    = DateTime.UtcNow,
            EventSummary = "測試事件",
            EventSource  = "HIS",
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        };
        _db.EmergencyEvents.Add(ev);
        await _db.SaveChangesAsync();

        var messages = Enumerable.Range(1, count).Select(i => new OutboxMessage
        {
            EventId     = ev.EventId,
            MessageType = OutboxMessageType.SendFlexMessage,
            PayloadJson = "{}",
            Status      = status,
            CreatedAt   = DateTime.UtcNow
        });

        _db.OutboxMessages.AddRange(messages);
        await _db.SaveChangesAsync();
    }

    private static HealthCheckContext MakeContext() =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "outbox-depth",
                _ => null!,
                HealthStatus.Unhealthy,
                Array.Empty<string>())
        };

    public void Dispose() => _db.Dispose();
}
