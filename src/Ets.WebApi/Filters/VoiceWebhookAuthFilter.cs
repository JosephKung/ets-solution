// src/Ets.WebApi/Filters/VoiceWebhookAuthFilter.cs
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Ets.Application.UseCases.Voice;

namespace Ets.WebApi.Filters;

/// <summary>
/// Voice Callback Webhook X-ETS-API-Key 驗證 Filter（WBS 1.4.4）
///
/// 驗證規則（§9.5.2）：
///   - 取 Header X-ETS-API-Key
///   - 取 RequestBody 中 external_call_id 之第 16 碼（event_type，a~e）
///   - 比對 TeamPlusChannels[event_type].ChannelSecret
///
/// 與 HIS X-ETS-API-Key 驗證（M2）共用相同的 ChannelSecret 對應表（§1.3 原則三）
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class VoiceWebhookAuthFilter : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<TeamPlusChannelsOptions>>().Value;

        // ── 取 X-ETS-API-Key Header ───────────────────────────────
        var apiKey = context.HttpContext.Request.Headers["X-ETS-API-Key"].ToString();
        if (string.IsNullOrEmpty(apiKey))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                success    = false,
                error_code = "E3001",
                message    = "Missing X-ETS-API-Key"
            });
            return;
        }

        // ── 取 external_call_id 的第 16 碼（event_type）─────────
        // external_call_id 格式：E20240101120000A001-{UserNo}-{RetryCount}
        // 第 16 碼（index 15）= event_type（a~e，大寫字母）
        // 規格書 §1：event_ID 格式 = E + YYYYMMDDHHMMSS(14碼) + event_type(1碼) + nnn(3碼)
        //           event_id 共 19 碼，第 16 碼（0-indexed: 15）= event_type
        string? externalCallId = null;

        // 從 Action Arguments 取 external_call_id
        // VoiceCallbackController 的 Action 應將 body 解析為含 external_call_id 的物件
        if (context.ActionArguments.TryGetValue("body", out var bodyObj) &&
            bodyObj is VoiceCallbackBody callbackBody)
        {
            externalCallId = callbackBody.ExternalCallId;
        }

        if (string.IsNullOrEmpty(externalCallId) || externalCallId.Length < 16)
        {
            context.Result = new BadRequestObjectResult(new
            {
                success    = false,
                error_code = "E3002",
                message    = "Missing or invalid external_call_id"
            });
            return;
        }

        var eventType = externalCallId[15].ToString().ToLowerInvariant();  // 第 16 碼

        // ── 比對 ChannelSecret ────────────────────────────────────
        if (!options.Channels.TryGetValue(eventType, out var channel) ||
            channel.ChannelSecret != apiKey)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                success    = false,
                error_code = "E3010",
                message    = "API Key 與 event_type 對應不符"
            });
            return;
        }

        await next();
    }
}

// /// <summary>
// /// Voice Callback Webhook Body（供 Filter 讀取 external_call_id）
// /// 完整定義在 VoiceCallbackController（1.4.5）
// /// </summary>
// public sealed class VoiceCallbackBody
// {
    // [System.Text.Json.Serialization.JsonPropertyName("external_call_id")]
    // public string ExternalCallId { get; init; } = string.Empty;

    // [System.Text.Json.Serialization.JsonPropertyName("status")]
    // public string Status { get; init; } = string.Empty;
// }
