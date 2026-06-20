// src/Ets.Infrastructure/BackgroundServices/OutboxDispatcherWorker.cs
using Ets.Application.Interfaces;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// Outbox Pattern 派送 Worker（WBS 1.3.15）
///
/// 每 3 秒掃描 OutboxMessages（Status=Pending 或 Failed 且到達重試時間）
/// 依 MessageType 路由至對應 IOutboxHandler
/// 失敗後以指數退避（5s * 2^RetryCount）排程下次重試，最多 5 次
///
/// 規格書參照：§11
/// </summary>
public sealed class OutboxDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherWorker> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const int MaxRetryCount = 5;
    private const int BatchSize     = 20;

    public OutboxDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("OutboxDispatcherWorker 啟動，輪詢間隔 {Interval}s",
            PollInterval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OutboxDispatcherWorker 批次例外");
            }

            await Task.Delay(PollInterval, ct);
        }

        _logger.LogInformation("OutboxDispatcherWorker 停止");
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope    = _scopeFactory.CreateScope();
        var db             = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handlerFactory = scope.ServiceProvider.GetRequiredService<IOutboxHandlerFactory>();

        var now = DateTime.UtcNow;

        // 撈取 Pending 或可重試的 Failed 訊息
        var messages = await db.OutboxMessages
            .Where(m =>
                m.ProcessedAt == null &&
                m.RetryCount  <  MaxRetryCount &&
                (m.Status == OutboxMessageStatus.Pending ||
                 (m.Status == OutboxMessageStatus.Failed &&
                  m.NextRetryAt <= now)))
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        _logger.LogDebug("OutboxDispatcher：批次 {Count} 筆", messages.Count);

        foreach (var msg in messages)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var handler = handlerFactory.GetHandler(msg.MessageType);
                await handler.HandleAsync(msg.OutboxId, msg.PayloadJson, ct);

                msg.Status      = OutboxMessageStatus.Done;
                msg.ProcessedAt = DateTime.UtcNow;
                msg.LastError   = null;

                _logger.LogInformation(
                    "Outbox 派送成功：OutboxId={OutboxId}, Type={Type}",
                    msg.OutboxId, msg.MessageType);
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.LastError   = ex.Message.Length > 500
                    ? ex.Message[..500]
                    : ex.Message;

                if (msg.RetryCount >= MaxRetryCount)
                {
                    msg.Status = OutboxMessageStatus.DeadLetterQueue;
                    _logger.LogError(ex,
                        "Outbox 達到最大重試次數，移至 DeadLetter：OutboxId={OutboxId}, Type={Type}",
                        msg.OutboxId, msg.MessageType);
                }
                else
                {
                    msg.Status      = OutboxMessageStatus.Failed;
                    // 指數退避：5s, 10s, 20s, 40s, 80s
                    msg.NextRetryAt = DateTime.UtcNow.AddSeconds(
                        Math.Pow(2, msg.RetryCount) * 5);

                    _logger.LogWarning(ex,
                        "Outbox 派送失敗，將重試（{RetryCount}/{Max}）：" +
                        "OutboxId={OutboxId}, Type={Type}, NextRetry={Next}",
                        msg.RetryCount, MaxRetryCount,
                        msg.OutboxId, msg.MessageType, msg.NextRetryAt);
                }
            }

            // 每筆獨立 SaveChanges（避免一筆失敗影響整批）
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Outbox SaveChanges 失敗：OutboxId={OutboxId}", msg.OutboxId);
            }
        }
    }
}
