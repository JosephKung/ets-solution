// tests/Ets.UnitTests/Infrastructure/TeamPlusSystemClientTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Infrastructure.ExternalClients.TeamPlus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;
using Xunit;
using Ets.Infrastructure.Resilience;

namespace Ets.UnitTests.Infrastructure;

public sealed class TeamPlusSystemClientTests
{
    // ─── 測試用假 HttpMessageHandler ───────────────────────────────
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
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    // ─── 建立受測 SUT ─────────────────────────────────────────────
    private static TeamPlusSystemClient CreateSut(string fakeJson,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler    = new FakeHttpHandler(fakeJson, statusCode);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://teamplus.hospital.internal")
        };

        var options = Options.Create(new TeamPlusSystemOptions
        {
            BaseUrl        = "https://teamplus.hospital.internal",
            SystemSn       = "28",
            ApiKey         = "test-api-key",
            TimeoutSeconds = 10
        });

        // 使用 pass-through Pipeline（單元測試不需真實重試）
        var pollyProvider = Substitute.For<ResiliencePipelineProvider<string>>();
        pollyProvider.GetPipeline(ResiliencePipelineKeys.TeamPlus)
                     .Returns(ResiliencePipeline.Empty);

        return new TeamPlusSystemClient(
            httpClient,
            options,
            pollyProvider,
            NullLogger<TeamPlusSystemClient>.Instance);
    }

    // ─── 測試 1：CreateTeamAsync 成功，正確映射 TeamSN ────────────
    [Fact]
    public async Task CreateTeamAsync_成功回應_應正確映射TeamSN與IgnoredList()
    {
        // Arrange
        var fakeResponse = JsonSerializer.Serialize(new
        {
            IsSuccess          = true,
            Description        = "Success",
            ErrorCode          = 0,
            TeamSN             = 99823104L,
            IgnoredMemberList  = new List<string> { "absent_user" },
            IgnoredManagerList = new List<string>()
        });

        var sut     = CreateSut(fakeResponse);
        var request = new CreateTeamRequest(
            Owner:       "joseph",
            Name:        "A0021 消防演習大樓火災緊急處理團隊",
            Subject:     "處理 A0021 消防演習大樓火災",
            Description: "發生火災，請立即應變",
            MemberList:  ["joseph", "mary"],
            ManagerList: ["joseph"]);

        // Act
        var result = await sut.CreateTeamAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TeamSN.Should().Be(99823104L);
        result.IgnoredMemberList.Should().ContainSingle().Which.Should().Be("absent_user");
        result.IgnoredManagerList.Should().BeEmpty();
        result.ErrorCode.Should().Be(0);
    }

    // ─── 測試 2：名稱超過 50 字應被截斷不拋出例外 ────────────────
    [Fact]
    public async Task CreateTeamAsync_名稱超過50字_應自動截斷不拋出例外()
    {
        // Arrange
        var fakeResponse = JsonSerializer.Serialize(new
        {
            IsSuccess = true, Description = "Success", ErrorCode = 0,
            TeamSN = 1L, IgnoredMemberList = new List<string>(), IgnoredManagerList = new List<string>()
        });

        var sut     = CreateSut(fakeResponse);
        var request = new CreateTeamRequest(
            Owner:       "joseph",
            Name:        new string('測', 100),   // 100 字，應被截斷為 50 字
            Subject:     "主旨",
            Description: "描述",
            MemberList:  ["joseph"],
            ManagerList: ["joseph"]);

        // Act & Assert
        var act = async () => await sut.CreateTeamAsync(request);
        await act.Should().NotThrowAsync();
    }

    // ─── 測試 3：team+ 回傳 HTTP 500 應拋出 HttpRequestException ──
    [Fact]
    public async Task CreateTeamAsync_HTTP500_應拋出HttpRequestException()
    {
        // Arrange
        var sut = CreateSut("{}", HttpStatusCode.InternalServerError);
        var request = new CreateTeamRequest(
            "joseph", "測試團隊", "主旨", "描述", ["joseph"], ["joseph"]);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.CreateTeamAsync(request));
    }

    // ─── 測試 4：CreateChatAsync 成功，正確映射 ChatSN ────────────
    [Fact]
    public async Task CreateChatAsync_成功回應_應正確映射ChatSN()
    {
        // Arrange
        var fakeResponse = JsonSerializer.Serialize(new
        {
            IsSuccess          = true,
            Description        = "Success",
            ErrorCode          = 0,
            ChatSN             = 88723611L,
            IgnoredMemberList  = new List<string>(),
            IgnoredManagerList = new List<string>()
        });

        var sut     = CreateSut(fakeResponse);
        var request = new CreateChatRequest(
            CreatorAccount: "joseph",
            ChatName:       "(A0021)消防組",
            MemberList:     ["joseph"],
            ManagerList:    ["joseph"]);

        // Act
        var result = await sut.CreateChatAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ChatSN.Should().Be(88723611L);
    }
}
