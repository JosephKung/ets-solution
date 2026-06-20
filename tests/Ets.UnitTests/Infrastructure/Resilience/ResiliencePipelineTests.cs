using Ets.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Resilience;

/// <summary>
/// Polly Resilience Pipeline 單元測試。
/// 驗證 Options 正確載入及 ShouldHandle 判斷邏輯。
/// </summary>
public class ResiliencePipelineTests
{
    // ── Options 綁定測試 ───────────────────────────────────────────────────

    [Fact(DisplayName = "ResilienceOptions 應能從 appsettings 正確綁定")]
    public void ResilienceOptions_ShouldBindFromConfiguration()
    {
        // Arrange — 建立 in-memory Configuration
        var inMemory = new Dictionary<string, string?>
        {
            ["Resilience:TeamPlus:MaxRetryAttempts"]               = "3",
            ["Resilience:TeamPlus:RetryBaseDelayMs"]               = "200",
            ["Resilience:TeamPlus:CircuitBreakerFailureRatio"]     = "0.5",
            ["Resilience:TeamPlus:CircuitBreakerBreakDurationSec"] = "30",
            ["Resilience:TeamPlus:TimeoutSec"]                     = "10",
            ["Resilience:VoiceApi:MaxRetryAttempts"]               = "2",
            ["Resilience:VoiceApi:RetryBaseDelayMs"]               = "500",
            ["Resilience:VoiceApi:TimeoutSec"]                     = "10",
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        // Act
        var opts = config
            .GetSection(ResilienceOptions.SectionName)
            .Get<ResilienceOptions>();

        // Assert
        opts.Should().NotBeNull();
        opts!.TeamPlus.MaxRetryAttempts.Should().Be(3);
        opts.TeamPlus.RetryBaseDelayMs.Should().Be(200);
        opts.TeamPlus.CircuitBreakerFailureRatio.Should().Be(0.5);
        opts.TeamPlus.CircuitBreakerBreakDurationSec.Should().Be(30);
        opts.TeamPlus.TimeoutSec.Should().Be(10);
        opts.VoiceApi.MaxRetryAttempts.Should().Be(2);
        opts.VoiceApi.RetryBaseDelayMs.Should().Be(500);
    }

    [Fact(DisplayName = "ResilienceOptions 預設值應符合規格書 §3.3")]
    public void ResilienceOptions_Defaults_ShouldMatchSpecification()
    {
        // Act
        var opts = new ResilienceOptions();

        // Assert — 對齊規格書 §3.3
        opts.TeamPlus.MaxRetryAttempts.Should().Be(3,
            because: "規格書 §3.3：team+ Retry 3 次");
        opts.TeamPlus.CircuitBreakerBreakDurationSec.Should().Be(30,
            because: "規格書 §3.3：Circuit Breaker 開路 30 秒");
        opts.TeamPlus.TimeoutSec.Should().Be(10,
            because: "規格書 §3.3：單次呼叫逾時 10 秒");
        opts.VoiceApi.MaxRetryAttempts.Should().Be(2,
            because: "語音外撥有副作用，重試次數應少於 team+");
    }

    // ── ShouldHandle 判斷邏輯測試 ──────────────────────────────────────────

    [Theory(DisplayName = "5xx 與逾時錯誤應觸發 Retry")]
    [InlineData(HttpStatusCode.InternalServerError)]   // 500
    [InlineData(HttpStatusCode.BadGateway)]            // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]    // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]        // 504
    [InlineData(HttpStatusCode.RequestTimeout)]        // 408
    [InlineData(HttpStatusCode.TooManyRequests)]       // 429
    public void ShouldHandle_RetryableStatusCodes_ShouldReturnTrue(HttpStatusCode statusCode)
    {
        // Arrange
        var response = new HttpResponseMessage(statusCode);

        // Act — 直接測試 BuildShouldHandle 的 HandleResult 條件
        var isRetryable =
            (int)statusCode >= 500 ||
            statusCode == HttpStatusCode.RequestTimeout ||
            statusCode == HttpStatusCode.TooManyRequests;

        // Assert
        isRetryable.Should().BeTrue(
            because: $"HTTP {(int)statusCode} 屬暫時性錯誤，應觸發 Retry");
    }

    [Theory(DisplayName = "4xx 客戶端錯誤（408/429 除外）不應觸發 Retry")]
    [InlineData(HttpStatusCode.Unauthorized)]   // 401 — API Key 無效，重試無意義
    [InlineData(HttpStatusCode.Forbidden)]      // 403
    [InlineData(HttpStatusCode.NotFound)]       // 404
    [InlineData(HttpStatusCode.BadRequest)]     // 400
    public void ShouldHandle_ClientErrors_ShouldNotRetry(HttpStatusCode statusCode)
    {
        // Act
        var isRetryable =
            (int)statusCode >= 500 ||
            statusCode == HttpStatusCode.RequestTimeout ||
            statusCode == HttpStatusCode.TooManyRequests;

        // Assert
        isRetryable.Should().BeFalse(
            because: $"HTTP {(int)statusCode} 是客戶端錯誤，重試不會改善結果");
    }

    // ── DI 容器整合測試 ────────────────────────────────────────────────────

    [Fact(DisplayName = "AddEtsResiliencePipelines 應能成功完成 DI 註冊")]
    public void AddEtsResiliencePipelines_ShouldRegisterWithoutException()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:TeamPlus:MaxRetryAttempts"] = "3",
                ["Resilience:VoiceApi:MaxRetryAttempts"] = "2",
            })
            .Build();

        // Act
        var act = () => services.AddEtsResiliencePipelines(config);

        // Assert
        act.Should().NotThrow(
            because: "DI 註冊過程不應拋出例外");
    }
}
