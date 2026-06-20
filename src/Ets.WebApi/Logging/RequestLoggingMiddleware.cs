using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Ets.WebApi.Logging;

/// <summary>
/// HTTP 請求結構化日誌 Middleware（WebApi 層）。
/// 記錄每個請求的 Method、Path、StatusCode、DurationMs 及 CorrelationId。
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/health/ready",
        "/health/startup",
        "/favicon.ico"
    };

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (SkipPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                         ?? Activity.Current?.TraceId.ToString()
                         ?? Guid.NewGuid().ToString("N");

        context.Response.Headers["X-Correlation-Id"] = correlationId;

        var sw = Stopwatch.StartNew();

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["ClientIp"]      = GetClientIp(context)
        }))
        {
            try
            {
                await _next(context);
            }
            finally
            {
                sw.Stop();
                var statusCode = context.Response.StatusCode;
                var durationMs = (int)sw.ElapsedMilliseconds;
                var method     = context.Request.Method;

                if (statusCode >= 500)
                {
                    _logger.LogError(
                        "HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms | CorrelationId={CorrelationId}",
                        method, path, statusCode, durationMs, correlationId);
                }
                else if (statusCode >= 400)
                {
                    _logger.LogWarning(
                        "HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms | CorrelationId={CorrelationId}",
                        method, path, statusCode, durationMs, correlationId);
                }
                else
                {
                    _logger.LogInformation(
                        "HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms | CorrelationId={CorrelationId}",
                        method, path, statusCode, durationMs, correlationId);
                }
            }
        }
    }

    private static string GetClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
