// src/Ets.WebApi/Controllers/VoiceCallbackController.cs
using Ets.Application.UseCases.Voice;
using Ets.Infrastructure.Webhooks;
using Ets.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Ets.WebApi.Controllers;

/// <summary>
/// Voice API Callback Webhook 接收端點（WBS 1.4.5）
/// Endpoint：POST /api/v1/webhooks/voicebot
/// 掛載 VoiceWebhookAuthFilter（§9.5.2 X-ETS-API-Key 驗證）
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public sealed class VoiceCallbackController : ControllerBase
{
    private readonly VoiceCallbackHandler _handler;
    private readonly ILogger<VoiceCallbackController> _logger;

    public VoiceCallbackController(
        VoiceCallbackHandler handler,
        ILogger<VoiceCallbackController> logger)
    {
        _handler = handler;
        _logger  = logger;
    }

    /// <summary>
    /// 接收 Voice API 8 階段 Callback（§9.5）
    /// 注意：voice API timeout 10 秒，必須快速回 200
    /// </summary>
    [HttpPost("voicebot")]
    [ServiceFilter(typeof(VoiceWebhookAuthFilter))]
    [Consumes("application/json")]
    public async Task<IActionResult> ReceiveCallback(
        [FromBody] VoiceCallbackBody body,
        CancellationToken ct)
    {
        try
        {
            await _handler.HandleAsync(body, ct);
        }
        catch (Exception ex)
        {
            // 即使處理失敗也回 200（§9.5 鐵律 ⑤：timeout 上限 10 秒）
            _logger.LogError(ex,
                "VoiceCallback 處理例外：ExternalCallId={CallId}, Status={Status}",
                body.ExternalCallId, body.Status);
        }

        return Ok(new { success = true });
    }
}
