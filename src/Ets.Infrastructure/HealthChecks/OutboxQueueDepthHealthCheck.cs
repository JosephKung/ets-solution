using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ets.Infrastructure.HealthChecks;

/// <summary>
/// Outbox 佇列深度健康檢查。
/// 對應規格書 §13.5 及院方監控告警閾值（§9.3）：
///   - Pending > 100  → Degraded（Medium 告警）
///   - Pending > 500  → Unhealthy（Critical 告警）
///   - DLQ > 0        → Unhealthy（Critical 告警，team+ 同步可能失敗）
/// </summary>
public sealed class OutboxQueueDepthHealthCheck : IHealthCheck
{
    private const int DegradedThreshold = 100;
    private const int UnhealthyThreshold = 500;

    private readonly AppDbContext _db;

    public OutboxQueueDepthHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 取得 Pending 數（Status = 0）
            var pendingCount = await _db.OutboxMessages
                .AsNoTracking()
                .CountAsync(m => m.Status == OutboxMessageStatus.Pending, cancellationToken);

            // 取得 DLQ 數（Status = 4）
            var dlqCount = await _db.OutboxMessages
                .AsNoTracking()
                .CountAsync(m => m.Status == OutboxMessageStatus.DeadLetterQueue, cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["outbox_pending"] = pendingCount,
                ["outbox_dlq"]     = dlqCount
            };

            // DLQ > 0 → Critical，代表有訊息永久無法派送
            if (dlqCount > 0)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox DLQ 有 {dlqCount} 筆死信，team+ 同步可能已中斷，請立即處理",
                    data: data);
            }

            // Pending > 500 → Critical
            if (pendingCount >= UnhealthyThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox 待處理數 {pendingCount} 已超過 {UnhealthyThreshold}，OutboxDispatcherWorker 可能異常",
                    data: data);
            }

            // Pending > 100 → Degraded（警告但仍可服務）
            if (pendingCount >= DegradedThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox 待處理數 {pendingCount} 超過 {DegradedThreshold}，建議檢查 team+ API 連通性",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Outbox 正常（pending={pendingCount}, dlq={dlqCount}）",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Outbox 健康檢查失敗（DB 查詢異常）",
                exception: ex);
        }
    }
}
