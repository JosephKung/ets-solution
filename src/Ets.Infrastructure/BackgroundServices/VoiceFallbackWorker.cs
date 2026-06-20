// src/Ets.Infrastructure/BackgroundServices/VoiceFallbackWorker.cs
using Ets.Application.Dtos.Voice;
using Ets.Application.Interfaces;
using Ets.Domain.Entities;
using Ets.Infrastructure.ExternalClients.Voice;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ets.Infrastructure.BackgroundServices;

/// <summary>
/// 語音 Fallback Worker（WBS 1.4.1 + 1.4.2）
///
/// 每 30 秒掃描一次 EventResponders，對滿足以下條件者發起語音外撥：
///   - ReplyStatus = 'Pending'（Flex 尚未回覆）
///   - VoiceRetryCount &lt; MaxRetryCount（未達重試上限）
///   - UserNo IS NOT NULL（§9.2.2 v3.2：UserNo 已解析）
///   - LastVoiceCallTime IS NULL 或距今 >= TimeoutMinutes（首次或超時）
///   - 所屬事件 Status = 0（Processing）
///
/// 外撥成功後 UPDATE EventResponders：
///   VoiceRetryCount + 1, LastVoiceCallTime = NOW, LastExternalCallId = {id}
///
/// 規格書參照：§9.1 / §9.2 / §9.3
/// </summary>
public sealed class VoiceFallbackWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoiceFallbackWorker> _logger;
    private readonly VoiceNotifyOptions _options;

    public VoiceFallbackWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<VoiceNotifyOptions> options,
        ILogger<VoiceFallbackWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options.Value;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "VoiceFallbackWorker 啟動，掃描週期 {Interval}s，外撥 timeout {Timeout}min",
            _options.ScanIntervalSeconds, _options.TimeoutMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanAndCallAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VoiceFallbackWorker 批次例外");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.ScanIntervalSeconds), ct);
        }

        _logger.LogInformation("VoiceFallbackWorker 停止");
    }

    private async Task ScanAndCallAsync(CancellationToken ct)
    {
        using var scope      = _scopeFactory.CreateScope();
        var db               = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var voiceClient      = scope.ServiceProvider.GetRequiredService<IVoiceApiClient>();

        var cutoff = DateTime.UtcNow.AddMinutes(-_options.TimeoutMinutes);

        // §9.2 觸發條件 SQL（LINQ 版本）
        var candidates = await db.EventResponders
            .Where(r =>
                r.ReplyStatus    == "Pending" &&
                r.VoiceRetryCount < _options.MaxRetryCount &&
                r.UserNo         != null &&   // v3.2：UserNo 未解析者跳過
                (r.LastVoiceCallTime == null ||
                 r.LastVoiceCallTime <= cutoff) &&
                db.EmergencyEvents.Any(e =>
                    e.EventId == r.EventId &&
                    e.Status  == Domain.Enums.EventStatus.Processing))
            .OrderBy(r => r.EventId)
            .ThenBy(r => r.ResponderId)
            .Take(_options.BatchSize)
            .Select(r => new
            {
                r.ResponderId,
                r.EventId,
                r.Account,
                r.UserNo,
                r.VoiceRetryCount,
                AudioContent = db.EmergencyEvents
                    .Where(e => e.EventId == r.EventId)
                    .Select(e => e.AudioContent)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogInformation("VoiceFallbackWorker：本批 {Count} 人待外撥", candidates.Count);

        foreach (var c in candidates)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await MakeSingleCallAsync(db, voiceClient, c.ResponderId,
                    c.EventId, c.Account, c.UserNo!.Value,
                    c.VoiceRetryCount, c.AudioContent ?? string.Empty, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VoiceFallbackWorker 單人外撥失敗：EventId={EventId}, Account={Account}",
                    c.EventId, c.Account);
            }
        }
    }

    private async Task MakeSingleCallAsync(
        AppDbContext db,
        IVoiceApiClient voiceClient,
        long responderId,
        string eventId,
        string account,
        int userNo,
        int currentRetryCount,
        string audioContent,
        CancellationToken ct)
    {
        var request = new VoiceCallRequest(
            EventId:       eventId,
            CalleeAccount: userNo.ToString(),   // §9.3 v3.2：UserNo 字串
            AudioContent:  audioContent,
            CallbackUrl:   _options.CallbackBaseUrl,
            RetryCount:    currentRetryCount + 1);

        var result = await voiceClient.CallAsync(request, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "Voice API 外撥失敗：EventId={EventId}, Account={Account}, UserNo={UserNo}",
                eventId, account, userNo);
            return;
        }

        // §9.3 後續動作：UPDATE EventResponders
        var responder = await db.EventResponders.FindAsync(
            new object[] { responderId }, ct);

        if (responder is null) return;

        responder.VoiceRetryCount   += 1;
        responder.LastVoiceCallTime  = DateTime.UtcNow;
        responder.LastExternalCallId = result.ExternalCallId;
        responder.LastVoiceStatus    = result.Status;  // "QUEUED"
        responder.LastVoiceStatusAt  = DateTime.UtcNow;
        responder.UpdatedAt          = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "VoiceFallback 外撥成功：EventId={EventId}, Account={Account}, " +
            "UserNo={UserNo}, ExternalCallId={CallId}, RetryCount={Retry}",
            eventId, account, userNo, result.ExternalCallId,
            responder.VoiceRetryCount);
    }
}
