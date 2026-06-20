using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace Ets.Infrastructure.Resilience;

/// <summary>
/// Polly v8 Resilience Pipeline 服務註冊擴充方法。
/// 提供 AddEtsResiliencePipelines() 供 InfrastructureServiceExtensions 呼叫。
///
/// 每個外部 HTTP Client 套用三層策略（由內到外執行）：
///   1. Timeout      — 單次呼叫最大等待時間
///   2. Retry        — 指數退避 + Jitter，僅重試暫時性錯誤
///   3. CircuitBreaker — 短路保護，防止雪崩效應
/// </summary>
public static class ResilienceServiceExtensions
{
    /// <summary>
    /// 為 IHttpClientBuilder 套用 ETS 標準 Resilience Pipeline。
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configuration">IConfiguration（讀取 Resilience 區段）</param>
    /// <returns>IServiceCollection</returns>
    public static IServiceCollection AddEtsResiliencePipelines(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 綁定 Options
        services.Configure<ResilienceOptions>(
            configuration.GetSection(ResilienceOptions.SectionName));

        var opts = configuration
            .GetSection(ResilienceOptions.SectionName)
            .Get<ResilienceOptions>() ?? new ResilienceOptions();

        // ── team+ API Pipeline ──────────────────────────────────────────
        // 透過具名 HttpClient 套用（M3 任務建立 TeamPlusClient 時使用）
        services
            .AddHttpClient(ResiliencePipelineKeys.TeamPlus)
            .AddResilienceHandler(
                ResiliencePipelineKeys.TeamPlus,
                pipeline => ConfigureTeamPlusPipeline(pipeline, opts.TeamPlus));

        // ── Voice API Pipeline ──────────────────────────────────────────
        services
            .AddHttpClient(ResiliencePipelineKeys.VoiceApi)
            .AddResilienceHandler(
                ResiliencePipelineKeys.VoiceApi,
                pipeline => ConfigureVoiceApiPipeline(pipeline, opts.VoiceApi));

        return services;
    }

    // ── team+ Pipeline 設定 ────────────────────────────────────────────────

    internal static void ConfigureTeamPlusPipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        TeamPlusResilienceOptions opts)
    {
        pipeline
            // 層 1（最外層）：Circuit Breaker
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // 在採樣期間內，失敗率超過閾值則開路
                FailureRatio      = opts.CircuitBreakerFailureRatio,
                SamplingDuration  = TimeSpan.FromSeconds(opts.CircuitBreakerSamplingDurationSec),
                MinimumThroughput = opts.CircuitBreakerMinimumThroughput,
                BreakDuration     = TimeSpan.FromSeconds(opts.CircuitBreakerBreakDurationSec),
                ShouldHandle      = BuildShouldHandle(),
                OnOpened = args =>
                {
                    // Circuit Breaker 開路時記錄警告
                    // 實際 logger 由 DI 注入，此處使用 Event Source 替代
                    Console.Error.WriteLine(
                        $"[ETS][CircuitBreaker] TeamPlus circuit OPENED for {args.BreakDuration.TotalSeconds}s. " +
                        $"Reason: {args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString()}");
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    Console.Error.WriteLine("[ETS][CircuitBreaker] TeamPlus circuit CLOSED — service recovered");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    Console.Error.WriteLine("[ETS][CircuitBreaker] TeamPlus circuit HALF-OPEN — probing");
                    return ValueTask.CompletedTask;
                }
            })

            // 層 2：Retry（指數退避 + Jitter）
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = opts.MaxRetryAttempts,
                Delay            = TimeSpan.FromMilliseconds(opts.RetryBaseDelayMs),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,   // 加入隨機 jitter，避免 thundering herd
                ShouldHandle     = BuildShouldHandle(),
                OnRetry = args =>
                {
                    Console.Error.WriteLine(
                        $"[ETS][Retry] TeamPlus retry #{args.AttemptNumber + 1} " +
                        $"after {args.RetryDelay.TotalMilliseconds:F0}ms. " +
                        $"Reason: {args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString()}");
                    return ValueTask.CompletedTask;
                }
            })

            // 層 3（最內層）：Timeout
            .AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSec));
    }

    // ── Voice API Pipeline 設定 ────────────────────────────────────────────

    internal static void ConfigureVoiceApiPipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        VoiceApiResilienceOptions opts)
    {
        pipeline
            // Circuit Breaker（語音 API 採樣時間較長）
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio      = opts.CircuitBreakerFailureRatio,
                SamplingDuration  = TimeSpan.FromSeconds(opts.CircuitBreakerSamplingDurationSec),
                MinimumThroughput = opts.CircuitBreakerMinimumThroughput,
                BreakDuration     = TimeSpan.FromSeconds(opts.CircuitBreakerBreakDurationSec),
                ShouldHandle      = BuildShouldHandle()
            })

            // Retry（語音外撥有副作用，重試次數較少）
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = opts.MaxRetryAttempts,
                Delay            = TimeSpan.FromMilliseconds(opts.RetryBaseDelayMs),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                ShouldHandle     = BuildShouldHandle()
            })

            // Timeout
            .AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSec));
    }

    // ── 共用：判斷是否應觸發 Retry / Circuit Breaker ──────────────────────

    /// <summary>
    /// 定義「值得重試」的條件：
    ///   - 任何 HttpRequestException（網路斷線、DNS 失敗等）
    ///   - HTTP 5xx（Server Error）
    ///   - HTTP 408 Request Timeout
    ///   - HTTP 429 Too Many Requests
    /// 注意：401/403/404 等客戶端錯誤不重試（代表請求本身有問題）。
    /// </summary>
    private static PredicateBuilder<HttpResponseMessage> BuildShouldHandle()
        => new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r =>
                (int)r.StatusCode >= 500 ||
                r.StatusCode == HttpStatusCode.RequestTimeout ||
                r.StatusCode == HttpStatusCode.TooManyRequests);
}
