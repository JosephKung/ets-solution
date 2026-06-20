using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ets.Infrastructure.Logging;

/// <summary>
/// ETS 業務 Metrics 定義。
/// 對應規格書 §13.3 業務 Metrics（Prometheus）。
///
/// 使用 .NET 8 內建 System.Diagnostics.Metrics（相容 OpenTelemetry）。
/// 各 Use Case 透過 DI 注入 EtsMetrics，呼叫對應方法記錄業務事件。
/// </summary>
public sealed class EtsMetrics : IDisposable
{
    /// <summary>Meter 名稱，供 OpenTelemetry AddMeter() 引用</summary>
    public const string MeterName = "ETS.Application";

    private readonly Meter _meter;

    // ── Counters ──────────────────────────────────────────────────────────

    /// <summary>事件建立累計次數（依 event_type 分標籤）</summary>
    private readonly Counter<long> _eventCreatedTotal;

    /// <summary>人員回覆次數（依 event_type + reply_status 分標籤）</summary>
    private readonly Counter<long> _responderReplyTotal;

    /// <summary>現場報到次數（依 event_type + chat_gp 分標籤）</summary>
    private readonly Counter<long> _checkInTotal;

    /// <summary>語音外撥次數（依結果 success/fail 分標籤）</summary>
    private readonly Counter<long> _voiceCallTotal;

    // ── Gauges ────────────────────────────────────────────────────────────

    /// <summary>Outbox 待處理訊息數（>100 應告警）</summary>
    private readonly ObservableGauge<int> _outboxPendingCount;

    /// <summary>Outbox 死信佇列數（>0 應立即告警）</summary>
    private readonly ObservableGauge<int> _outboxDlqCount;

    /// <summary>SignalR 即時連線數</summary>
    private readonly ObservableGauge<int> _signalrConnectionsActive;

    // ── 外部注入的即時值 getter ──────────────────────────────────────────

    private Func<int> _getOutboxPendingCount = () => 0;
    private Func<int> _getOutboxDlqCount     = () => 0;
    private Func<int> _getSignalrConnections = () => 0;

    public EtsMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _eventCreatedTotal = _meter.CreateCounter<long>(
            "ets_event_created_total",
            description: "緊急事件建立累計次數");

        _responderReplyTotal = _meter.CreateCounter<long>(
            "ets_responder_reply_total",
            description: "應變人員回覆累計次數");

        _checkInTotal = _meter.CreateCounter<long>(
            "ets_checkin_total",
            description: "現場報到累計次數");

        _voiceCallTotal = _meter.CreateCounter<long>(
            "ets_voice_call_total",
            description: "語音外撥累計次數");

        _outboxPendingCount = _meter.CreateObservableGauge(
            "ets_outbox_pending_count",
            () => _getOutboxPendingCount(),
            description: "Outbox 待處理訊息數（>100 應告警）");

        _outboxDlqCount = _meter.CreateObservableGauge(
            "ets_outbox_dlq_count",
            () => _getOutboxDlqCount(),
            description: "Outbox 死信佇列數（>0 應立即告警）");

        _signalrConnectionsActive = _meter.CreateObservableGauge(
            "ets_signalr_connections_active",
            () => _getSignalrConnections(),
            description: "SignalR 即時連線數");
    }

    // ── 業務事件記錄方法 ──────────────────────────────────────────────────

    /// <summary>記錄事件建立</summary>
    public void RecordEventCreated(string eventType)
        => _eventCreatedTotal.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    /// <summary>記錄人員回覆</summary>
    public void RecordResponderReply(string eventType, string replyStatus)
        => _responderReplyTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("reply_status", replyStatus));

    /// <summary>記錄現場報到</summary>
    public void RecordCheckIn(string eventType, string chatGp)
        => _checkInTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("chat_gp", chatGp));

    /// <summary>記錄語音外撥結果</summary>
    public void RecordVoiceCall(bool success)
        => _voiceCallTotal.Add(1,
            new KeyValuePair<string, object?>("result", success ? "success" : "fail"));

    // ── Gauge 值注冊方法（由 Worker 或 HostedService 呼叫）──────────────

    /// <summary>注冊 Outbox Pending 計數 getter（由 OutboxDispatcherWorker 注冊）</summary>
    public void RegisterOutboxPendingGetter(Func<int> getter)
        => _getOutboxPendingCount = getter;

    /// <summary>注冊 Outbox DLQ 計數 getter</summary>
    public void RegisterOutboxDlqGetter(Func<int> getter)
        => _getOutboxDlqCount = getter;

    /// <summary>注冊 SignalR 連線數 getter（由 DashboardHub 注冊）</summary>
    public void RegisterSignalrConnectionsGetter(Func<int> getter)
        => _getSignalrConnections = getter;

    public void Dispose() => _meter.Dispose();
}
