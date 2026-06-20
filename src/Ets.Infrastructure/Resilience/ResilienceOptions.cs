namespace Ets.Infrastructure.Resilience;

/// <summary>
/// Polly Resilience Pipeline 全域設定參數。
/// 從 appsettings.json 的 "Resilience" 區段載入。
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>team+ API Client Resilience 設定</summary>
    public TeamPlusResilienceOptions TeamPlus { get; set; } = new();

    /// <summary>Voice API Client Resilience 設定</summary>
    public VoiceApiResilienceOptions VoiceApi { get; set; } = new();
}

/// <summary>team+ API 專用 Resilience 參數</summary>
public sealed class TeamPlusResilienceOptions
{
    /// <summary>Retry 最大次數（預設 3）</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Retry 基礎延遲毫秒（指數退避，預設 200ms）</summary>
    public int RetryBaseDelayMs { get; set; } = 200;

    /// <summary>Circuit Breaker：樣本期間內失敗率閾值（0.5 = 50%）</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Circuit Breaker：採樣期間（秒，預設 30）</summary>
    public int CircuitBreakerSamplingDurationSec { get; set; } = 30;

    /// <summary>Circuit Breaker：最小吞吐量（需達此次數才開始計算失敗率）</summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>Circuit Breaker：開路持續時間（秒，預設 30）</summary>
    public int CircuitBreakerBreakDurationSec { get; set; } = 30;

    /// <summary>單次呼叫逾時（秒，預設 10）</summary>
    public int TimeoutSec { get; set; } = 10;
}

/// <summary>Voice API 專用 Resilience 參數（較保守，因語音外撥有副作用）</summary>
public sealed class VoiceApiResilienceOptions
{
    /// <summary>Retry 最大次數（預設 2，語音外撥副作用不宜過多重試）</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Retry 基礎延遲毫秒（預設 500ms）</summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>Circuit Breaker：失敗率閾值</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Circuit Breaker：採樣期間（秒）</summary>
    public int CircuitBreakerSamplingDurationSec { get; set; } = 60;

    /// <summary>Circuit Breaker：最小吞吐量</summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 3;

    /// <summary>Circuit Breaker：開路持續時間（秒，預設 60）</summary>
    public int CircuitBreakerBreakDurationSec { get; set; } = 60;

    /// <summary>單次呼叫逾時（秒，預設 10）</summary>
    public int TimeoutSec { get; set; } = 10;
}
