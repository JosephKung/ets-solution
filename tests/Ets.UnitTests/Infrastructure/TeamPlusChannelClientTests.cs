// tests/Ets.UnitTests/Infrastructure/TeamPlusChannelClientTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
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

public sealed class TeamPlusChannelClientTests
{
    // ─── 假 HttpMessageHandler ─────────────────────────────────────
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        // 捕捉最後一次請求，供測試驗證
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHttpHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode   = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    // ─── 建立 SUT ─────────────────────────────────────────────────
    private static (TeamPlusChannelClient Sut, FakeHttpHandler Handler) CreateSut(
        string fakeJson,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler    = new FakeHttpHandler(fakeJson, statusCode);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://teamplus.hospital.internal")
        };

        var channelsOptions = Options.Create(new TeamPlusChannelsOptions
        {
            Channels = new Dictionary<string, TeamPlusChannelEntry>
            {
                ["a"] = new TeamPlusChannelEntry
                {
                    ChannelId     = "180284",
                    ChannelSecret = "test-secret",
                    AccessToken   = "test-access-token"
                }
            }
        });

        var systemOptions = Options.Create(new TeamPlusSystemOptions
        {
            BaseUrl        = "https://teamplus.hospital.internal",
            SystemSn       = "28",
            ApiKey         = "test-api-key",
            TimeoutSeconds = 10
        });

        var pollyProvider = Substitute.For<ResiliencePipelineProvider<string>>();
        pollyProvider.GetPipeline(ResiliencePipelineKeys.TeamPlus)
                     .Returns(ResiliencePipeline.Empty);

        var sut = new TeamPlusChannelClient(
            httpClient,
            channelsOptions,
            systemOptions,
            pollyProvider,
            NullLogger<TeamPlusChannelClient>.Instance);

        return (sut, handler);
    }

    // ─── 測試 1：BroadcastFlexMessageAsync 成功，正確回傳 MessageSN ──
    [Fact]
    public async Task BroadcastFlexMessageAsync_成功回應_應正確回傳MessageSN()
    {
        // Arrange
        var fakeJson = JsonSerializer.Serialize(new { MessageSN = 9453 });
        var (sut, handler) = CreateSut(fakeJson);

        var request = new BroadcastFlexMessageRequest(
            EventType:     "a",
            RecipientList: ["joseph", "mary"],
            FlexContents:  new { body = new[] { new { type = "text", text = "test" } } });

        // Act
        var result = await sut.BroadcastFlexMessageAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.MessageSN.Should().Be(9453);

        // 驗證使用 Bearer Token 認證（§6.1.3）
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("test-access-token");
    }

    // ─── 測試 2：GetMsgReadStatusAsync 成功，正確回傳已讀清單 ───────
    [Fact]
    public async Task GetMsgReadStatusAsync_成功回應_應正確回傳ReadCount與明細()
    {
        // Arrange
        var fakeJson = JsonSerializer.Serialize(new
        {
            ReadCount      = 2,
            ReadDetailList = new[]
            {
                new { Account = "joseph", ReadTime = "2024-01-01T12:01:23+08:00" },
                new { Account = "peter",  ReadTime = "2024-01-01T12:02:45+08:00" }
            }
        });

        var (sut, _) = CreateSut(fakeJson);
        var request  = new GetMsgReadStatusRequest(EventType: "a", MessageSN: 9453);

        // Act
        var result = await sut.GetMsgReadStatusAsync(request);

        // Assert
        result.ReadCount.Should().Be(2);
        result.ReadDetailList.Should().HaveCount(2);
        result.ReadDetailList[0].Account.Should().Be("joseph");
    }

    // ─── 測試 3：UpdateFlexFooterAsync 成功回傳 ───────────────────
    [Fact]
    public async Task UpdateFlexFooterAsync_成功回應_IsSuccess應為True()
    {
        // Arrange
        var fakeJson = JsonSerializer.Serialize(new
        {
            IsSuccess   = true,
            Description = "Success",
            ErrorCode   = 0
        });

        var (sut, _) = CreateSut(fakeJson);
        var request  = new UpdateFlexFooterRequest(
            EventType:   "a",
            MessageSN:   9453,
            Recipient:   "marry",
            FooterText:  "已送出！",
            FontColor:   "#E53935");

        // Act
        var result = await sut.UpdateFlexFooterAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ErrorCode.Should().Be(0);
    }

    // ─── 測試 4：event_type 不存在應拋出 InvalidOperationException ─
    [Fact]
    public async Task BroadcastFlexMessageAsync_未知EventType_應拋出InvalidOperationException()
    {
        // Arrange
        var (sut, _) = CreateSut("{\"MessageSN\":1}");
        var request  = new BroadcastFlexMessageRequest(
            EventType:     "z",   // 不存在的 event_type
            RecipientList: ["joseph"],
            FlexContents:  new { });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.BroadcastFlexMessageAsync(request));
    }
}
