using Ets.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ets.WebApi.Middlewares;

/// <summary>
/// HIS API Key 認證 Middleware。
/// 對應規格書 §5.1 三層防護（傳輸層由 TLS 負責，此處實作後兩層）：
///   層 2 — X-ETS-API-Key Header 存在性檢查（event_type 比對在 Controller）
///   層 3 — IP 白名單驗證
///
/// 僅攔截 /api/v1/his/* 路徑，其他路徑直接放行。
///
/// 注意：API Key 與 event_type 的匹配驗證需要 Request Body，
///       因此移至 EventTriggerController 執行（Body 需要先反序列化）。
///       此 Middleware 只做：IP 白名單 + Header 存在性。
/// </summary>
public sealed class HisApiKeyMiddleware
{
    private const string ApiKeyHeader   = "X-ETS-API-Key";
    private const string HisPathPrefix  = "/api/v1/his/";

    private readonly RequestDelegate _next;
    private readonly IIpWhitelistService _ipWhitelist;
    private readonly ILogger<HisApiKeyMiddleware> _logger;

    public HisApiKeyMiddleware(
        RequestDelegate next,
        IIpWhitelistService ipWhitelist,
        ILogger<HisApiKeyMiddleware> logger)
    {
        _next        = next;
        _ipWhitelist = ipWhitelist;
        _logger      = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 只攔截 /api/v1/his/* 路徑，其他直接放行
        if (!path.StartsWith(HisPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // ── 層 3：IP 白名單 ───────────────────────────────────────────────────
        var clientIp = GetClientIp(context);

        if (!_ipWhitelist.IsAllowed(clientIp))
        {
            _logger.LogWarning(
                "HIS API 請求被 IP 白名單拒絕：ClientIP={ClientIp}, Path={Path}",
                clientIp, path);

            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "E3009",
                $"IP address '{clientIp}' is not in the allowed list");
            return;
        }

        // ── 層 2：API Key Header 存在性 ──────────────────────────────────────
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValues) ||
            string.IsNullOrWhiteSpace(apiKeyValues.ToString()))
        {
            _logger.LogWarning(
                "HIS API 請求缺少 {Header}：ClientIP={ClientIp}, Path={Path}",
                ApiKeyHeader, clientIp, path);

            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "E3001",
                $"Missing required header: {ApiKeyHeader}");
            return;
        }

        // 將 API Key 暫存於 HttpContext.Items，供 Controller 使用
        context.Items[ApiKeyHeader] = apiKeyValues.ToString();

        _logger.LogDebug(
            "HIS API 請求通過 Middleware 驗證：ClientIP={ClientIp}, Path={Path}",
            clientIp, path);

        await _next(context);
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 取得真實用戶端 IP。
    /// 優先讀取 X-Forwarded-For（反向代理場景），其次讀取 RemoteIpAddress。
    /// </summary>
    private static string GetClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// 輸出標準 ETS 錯誤 JSON 回應。
    /// </summary>
    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message)
    {
        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            success       = false,
            error_code    = errorCode,
            error_message = message,
            data          = (object?)null
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}
