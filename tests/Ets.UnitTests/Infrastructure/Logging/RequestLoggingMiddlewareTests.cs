using Ets.WebApi.Logging;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Logging;

/// <summary>
/// RequestLoggingMiddleware 單元測試。
/// </summary>
public class RequestLoggingMiddlewareTests
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddlewareTests()
    {
        _logger = Substitute.For<ILogger<RequestLoggingMiddleware>>();
    }

    [Theory(DisplayName = "健康檢查路徑應略過 Log，直接傳遞至下一個 Middleware")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health/startup")]
    [InlineData("/favicon.ico")]
    public async Task HealthCheckPaths_ShouldSkipLogging_AndPassThrough(string path)
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var middleware = new RequestLoggingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue(because: $"路徑 {path} 應直接傳遞至下一個 Middleware");
        _logger.ReceivedCalls().Should().BeEmpty(
            because: "健康檢查路徑不應產生任何 log");
    }

    [Fact(DisplayName = "一般 API 路徑應在回應 Header 加入 X-Correlation-Id")]
    public async Task ApiPath_ShouldInjectCorrelationIdHeader()
    {
        // Arrange
        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path   = "/api/v1/test";
        context.Response.Body  = new System.IO.MemoryStream();

        var middleware = new RequestLoggingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.ContainsKey("X-Correlation-Id")
            .Should().BeTrue();
        context.Response.Headers["X-Correlation-Id"].ToString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "若請求帶有 X-Correlation-Id，回應應繼承同一個值")]
    public async Task Request_WithExistingCorrelationId_ShouldPreserveIt()
    {
        // Arrange
        const string existingId = "test-correlation-12345";

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var context = new DefaultHttpContext();
        context.Request.Method  = "POST";
        context.Request.Path    = "/api/v1/his/event-trigger";
        context.Request.Headers["X-Correlation-Id"] = existingId;
        context.Response.Body = new System.IO.MemoryStream();

        var middleware = new RequestLoggingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["X-Correlation-Id"].ToString()
            .Should().Be(existingId);
    }
}
