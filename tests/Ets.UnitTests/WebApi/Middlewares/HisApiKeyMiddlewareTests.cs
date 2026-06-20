using Ets.Application.Abstractions;
using Ets.WebApi.Middlewares;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace Ets.UnitTests.WebApi.Middlewares;

/// <summary>
/// HisApiKeyMiddleware 單元測試。
/// 覆蓋規格書 §5.1 三種主要情境：IP 拒絕、Header 缺失、通過驗證。
/// </summary>
public class HisApiKeyMiddlewareTests
{
    private readonly IIpWhitelistService _ipWhitelist;
    private readonly ILogger<HisApiKeyMiddleware> _logger;

    public HisApiKeyMiddlewareTests()
    {
        _ipWhitelist = Substitute.For<IIpWhitelistService>();
        _logger      = Substitute.For<ILogger<HisApiKeyMiddleware>>();
    }

    // ── 情境 1：非 HIS 路徑應直接放行（不做任何驗證）────────────────────────

    [Theory(DisplayName = "非 /api/v1/his/* 路徑應直接放行")]
    [InlineData("/health/live")]
    [InlineData("/api/v1/webhooks/teamplus/postback")]
    [InlineData("/swagger/index.html")]
    public async Task NonHisPath_ShouldPassThrough_WithoutValidation(string path)
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };

        var context = MakeContext(path, remoteIp: "1.2.3.4");
        var middleware = new HisApiKeyMiddleware(next, _ipWhitelist, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue(because: $"路徑 {path} 非 HIS 入口，不應被攔截");
        _ipWhitelist.DidNotReceive().IsAllowed(Arg.Any<string>());
    }

    // ── 情境 2：IP 不在白名單 → 403 E3009 ────────────────────────────────────

    [Fact(DisplayName = "IP 不在白名單時應回 403 E3009")]
    public async Task BlockedIp_ShouldReturn_403_E3009()
    {
        // Arrange
        _ipWhitelist.IsAllowed(Arg.Any<string>()).Returns(false);

        var context = MakeContext("/api/v1/his/event-trigger", remoteIp: "8.8.8.8");
        context.Response.Body = new System.IO.MemoryStream();

        var middleware = new HisApiKeyMiddleware(_ => Task.CompletedTask, _ipWhitelist, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403,
            because: "IP 不在白名單應回 403，對應錯誤碼 E3009");

        var body = await ReadResponseBodyAsync(context);
        body.Should().Contain("E3009",
            because: "回應 JSON 應包含 error_code E3009");
    }

    // ── 情境 3：IP 允許但缺少 X-ETS-API-Key → 401 E3001 ─────────────────────

    [Fact(DisplayName = "IP 允許但缺少 X-ETS-API-Key Header 應回 401 E3001")]
    public async Task AllowedIp_MissingApiKeyHeader_ShouldReturn_401_E3001()
    {
        // Arrange
        _ipWhitelist.IsAllowed(Arg.Any<string>()).Returns(true);

        var context = MakeContext("/api/v1/his/event-trigger", remoteIp: "10.0.1.5");
        context.Response.Body = new System.IO.MemoryStream();
        // 刻意不加 X-ETS-API-Key Header

        var middleware = new HisApiKeyMiddleware(_ => Task.CompletedTask, _ipWhitelist, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401,
            because: "缺少 API Key Header 應回 401，對應錯誤碼 E3001");

        var body = await ReadResponseBodyAsync(context);
        body.Should().Contain("E3001");
    }

    // ── 情境 4：IP 允許 + API Key 存在 → 放行並暫存 Key ─────────────────────

    [Fact(DisplayName = "IP 允許且帶有 X-ETS-API-Key 應放行並暫存 Key 至 HttpContext.Items")]
    public async Task AllowedIp_WithApiKeyHeader_ShouldPassThrough_AndStoreKey()
    {
        // Arrange
        _ipWhitelist.IsAllowed(Arg.Any<string>()).Returns(true);

        const string apiKey = "test-channel-secret-abc";
        var nextCalled      = false;
        HttpContext? capturedContext = null;

        RequestDelegate next = ctx =>
        {
            nextCalled      = true;
            capturedContext = ctx;
            return Task.CompletedTask;
        };

        var context = MakeContext("/api/v1/his/event-trigger", remoteIp: "10.0.1.5");
        context.Request.Headers["X-ETS-API-Key"] = apiKey;

        var middleware = new HisApiKeyMiddleware(next, _ipWhitelist, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue(because: "驗證通過應呼叫下一個 Middleware");
        capturedContext!.Items["X-ETS-API-Key"].Should().Be(apiKey,
            because: "API Key 應暫存於 HttpContext.Items 供 Controller 讀取");
    }

    // ── 情境 5：X-Forwarded-For 場景（反向代理後方）─────────────────────────

    [Fact(DisplayName = "應優先讀取 X-Forwarded-For 作為客戶端 IP")]
    public async Task ForwardedIp_ShouldBeUsedForWhitelistCheck()
    {
        // Arrange
        string? capturedIp = null;
        _ipWhitelist.IsAllowed(Arg.Do<string?>(ip => capturedIp = ip)).Returns(true);

        var context = MakeContext("/api/v1/his/event-trigger", remoteIp: "192.168.1.1");
        context.Request.Headers["X-Forwarded-For"] = "10.0.1.50, 192.168.1.1";
        context.Request.Headers["X-ETS-API-Key"]   = "some-key";

        var middleware = new HisApiKeyMiddleware(_ => Task.CompletedTask, _ipWhitelist, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedIp.Should().Be("10.0.1.50",
            because: "X-Forwarded-For 的第一個 IP 才是真實客戶端 IP");
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────────

    private static DefaultHttpContext MakeContext(string path, string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
        using var reader = new System.IO.StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
