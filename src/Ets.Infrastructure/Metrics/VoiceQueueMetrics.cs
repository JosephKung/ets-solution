// src/Ets.Infrastructure/Metrics/VoiceQueueMetrics.cs
using Ets.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Metrics;

/// <summary>
/// 語音 API 佇列指標實作（WBS 1.4.3）
///
/// 觸發條件（§9.3 佇列觀察建議）：
///   - queue.waiting > 0：有排隊等待，表示語音 API 已達並發上限
///   - queue.in_use >= queue.max * 0.8：使用率 ≥ 80%，即將壅塞
///
/// 目前以 Serilog structured log 實作（供 Application Insights / Grafana 攝取）
/// 後續可擴充為 Prometheus Counter / Gauge
/// </summary>
public sealed class VoiceQueueMetrics : IVoiceQueueMetrics
{
    private readonly ILogger<VoiceQueueMetrics> _logger;

    /// <summary>壅塞警告閾值（使用率 80%）</summary>
    private const double CongestionThreshold = 0.8;

    public VoiceQueueMetrics(ILogger<VoiceQueueMetrics> logger)
    {
        _logger = logger;
    }

    public void RecordQueueStatus(int inUse, int max, int waiting)
    {
        // 結構化 log，供 Application Insights 建立 Alert Rule
        _logger.LogInformation(
            "VoiceQueue status: InUse={InUse}, Max={Max}, Waiting={Waiting}, " +
            "Utilization={Utilization:P0}",
            inUse, max, waiting,
            max > 0 ? (double)inUse / max : 0d);

        // 壅塞告警（Log Level = Warning，Application Insights 可設 Alert）
        if (waiting > 0)
        {
            _logger.LogWarning(
                "VoiceQueue 壅塞警告：佇列有 {Waiting} 筆等待中，" +
                "語音 API 已達並發上限（InUse={InUse}/{Max}）。" +
                "建議通知語音 API 維運人員擴充並發容量。",
                waiting, inUse, max);
        }
        else if (max > 0 && inUse >= max * CongestionThreshold)
        {
            _logger.LogWarning(
                "VoiceQueue 接近壅塞：使用率 {Utilization:P0}（{InUse}/{Max}），" +
                "建議關注後續外撥是否排隊。",
                (double)inUse / max, inUse, max);
        }
    }
}
