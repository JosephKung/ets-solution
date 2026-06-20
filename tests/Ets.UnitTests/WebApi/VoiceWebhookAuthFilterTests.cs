// tests/Ets.UnitTests/WebApi/VoiceWebhookAuthFilterTests.cs
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Ets.WebApi.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Ets.Application.UseCases.Voice;

namespace Ets.UnitTests.WebApi;

public sealed class VoiceWebhookAuthFilterTests
{
    private static ActionExecutingContext BuildContext(
        string? apiKey,
        string? externalCallId,
        TeamPlusChannelsOptions? channelOptions = null)
    {
        channelOptions ??= new TeamPlusChannelsOptions
        {
            Channels = new Dictionary<string, TeamPlusChannelEntry>
            {
                ["a"] = new TeamPlusChannelEntry
                {
                    ChannelId     = "test-channel-a",
                    ChannelSecret = "test-channel-secret-a",
                    AccessToken   = "test-token-a"
                }
            }
        };

        var services = new ServiceCollection();
        services.Configure<TeamPlusChannelsOptions>(o =>
        {
            foreach (var kv in channelOptions.Channels)
                o.Channels[kv.Key] = kv.Value;
        });

        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        if (apiKey is not null)
            httpContext.Request.Headers["X-ETS-API-Key"] = apiKey;

        var actionDescriptor  = new ActionDescriptor();
        var routeData         = new RouteData();
        var actionContext      = new ActionContext(httpContext, routeData, actionDescriptor);
        var filters            = new List<IFilterMetadata>();
        var actionArguments    = new Dictionary<string, object?>();

        if (externalCallId is not null)
            actionArguments["body"] = new VoiceCallbackBody
            {
                ExternalCallId = externalCallId,
                Status         = "QUEUED"
            };

        return new ActionExecutingContext(
            actionContext, filters, actionArguments, controller: null!);
    }

    // ─── 測試 1：合法 Key + 正確 event_type → 應通過 ─────────────
    [Fact]
    public async Task Filter_合法ApiKey_應通過驗證()
    {
        // external_call_id 第 16 碼 = 'a'（index 15）
        // E20240101120000A001 → 長度 19，index 15 = 'A'（event_type 大寫）
        // 實際 external_call_id 格式：E20240101120000A001-9-1
        // 第 16 碼（index 15）= 'A' → toLower = 'a'
        var externalCallId = "E20240101120000A001-9-1";
        var context        = BuildContext("test-channel-secret-a", externalCallId);
        var filter         = new VoiceWebhookAuthFilter();
        var wasCalled      = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            wasCalled = true;
            return Task.FromResult(
                new ActionExecutedContext(
                    context, new List<IFilterMetadata>(), null!));
        });

        wasCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    // ─── 測試 2：缺少 Header → 應回 401 E3001 ───────────────────
    [Fact]
    public async Task Filter_缺少ApiKeyHeader_應回401E3001()
    {
        var context = BuildContext(null, "E20240101120000A001-9-1");
        var filter  = new VoiceWebhookAuthFilter();

        await filter.OnActionExecutionAsync(context, () =>
            Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), null!)));

        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
        var result = (context.Result as UnauthorizedObjectResult)!;
        result.Value.Should().BeEquivalentTo(new
        {
            success    = false,
            error_code = "E3001"
        });
    }

    // ─── 測試 3：Key 與 event_type 不符 → 應回 401 E3010 ────────
    [Fact]
    public async Task Filter_ApiKey與EventType不符_應回401E3010()
    {
        // 使用 'a' 的 Key，但 external_call_id 的 event_type 是 'b'（index 15 = 'B'）
        var externalCallId = "E20240101120000B001-9-1";  // 第 16 碼 = 'B'
        var context        = BuildContext("test-channel-secret-a", externalCallId);
        var filter         = new VoiceWebhookAuthFilter();

        await filter.OnActionExecutionAsync(context, () =>
            Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), null!)));

        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
        var result = (context.Result as UnauthorizedObjectResult)!;
        result.Value.Should().BeEquivalentTo(new
        {
            success    = false,
            error_code = "E3010"
        });
    }

    // ─── 測試 4：external_call_id 長度不足 → 應回 400 E3002 ─────
    [Fact]
    public async Task Filter_ExternalCallId長度不足_應回400E3002()
    {
        var context = BuildContext("test-channel-secret-a", "short");
        var filter  = new VoiceWebhookAuthFilter();

        await filter.OnActionExecutionAsync(context, () =>
            Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), null!)));

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
