// tests/Ets.UnitTests/Infrastructure/TeamPlusSsoClientTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Ets.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;
using Xunit;

namespace Ets.UnitTests.Infrastructure;

public sealed class TeamPlusSsoClientTests
{
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public FakeHttpHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode   = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            });
    }

    private static TeamPlusSsoClient CreateSut(
        string fakeJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var httpClient = new HttpClient(new FakeHttpHandler(fakeJson, statusCode))
        {
            BaseAddress = new Uri("https://teamplus.hospital.internal")
        };

        var options = Options.Create(new TeamPlusSsoOptions
        {
            BaseUrl        = "https://teamplus.hospital.internal",
            SystemSn       = "1",
            ApiKey         = "test-sso-key",
            TimeoutSeconds = 10
        });

        var pollyProvider = Substitute.For<ResiliencePipelineProvider<string>>();
        pollyProvider.GetPipeline(ResiliencePipelineKeys.TeamPlus)
                     .Returns(ResiliencePipeline.Empty);

        return new TeamPlusSsoClient(
            httpClient,
            options,
            pollyProvider,
            NullLogger<TeamPlusSsoClient>.Instance);
    }

    // ─── 測試 1：SSO 成功，正確回傳 UserAccount ───────────────────
    [Fact]
    public async Task GetUserAccountAsync_成功回應_應正確回傳UserAccount()
    {
        // Arrange
        var fakeJson = JsonSerializer.Serialize(new
        {
            IsSuccess   = true,
            Description = "Success",
            ErrorCode   = 0,
            UserAccount = "joseph"
        });

        var sut    = CreateSut(fakeJson);

        // Act
        var result = await sut.GetUserAccountAsync("valid-session-key-guid");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.UserAccount.Should().Be("joseph");
        result.ErrorCode.Should().Be(0);
    }

    // ─── 測試 2：session_key 過期（ErrorCode = -11）───────────────
    [Fact]
    public async Task GetUserAccountAsync_SessionKeyExpired_IsSuccess應為False且ErrorCode為負11()
    {
        // Arrange
        var fakeJson = JsonSerializer.Serialize(new
        {
            IsSuccess   = false,
            Description = "Session key expired",
            ErrorCode   = -11,
            UserAccount = (string?)null
        });

        var sut    = CreateSut(fakeJson);

        // Act
        var result = await sut.GetUserAccountAsync("expired-session-key");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(-11);
        result.UserAccount.Should().BeEmpty();
    }

    // ─── 測試 3：空 session_key 應拋出 ArgumentException ──────────
    [Fact]
    public async Task GetUserAccountAsync_空SessionKey_應拋出ArgumentException()
    {
        var sut = CreateSut("{}");

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GetUserAccountAsync(string.Empty));
    }

    // ─── 測試 4：team+ 回傳 HTTP 500 應拋出 HttpRequestException ──
    [Fact]
    public async Task GetUserAccountAsync_HTTP500_應拋出HttpRequestException()
    {
        var sut = CreateSut("{}", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.GetUserAccountAsync("some-session-key"));
    }
}
