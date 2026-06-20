// src/Ets.WebApi/Controllers/TeamPlusWebhookController.cs
using System.Text;
using Ets.Application.Interfaces;
using Ets.Application.UseCases.Webhooks;
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Ets.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ets.WebApi.Controllers;

/// <summary>
/// team+ Postback Webhook 接收端點（§7.1）
/// Endpoint：POST /api/v1/webhooks/teamplus/postback
/// </summary>
[ApiController]
[Route("api/v1/webhooks/teamplus")]
public sealed class TeamPlusWebhookController : ControllerBase
{
    private readonly ITeamPlusSignatureVerifier _verifier;
    private readonly PostbackWebhookHandler _handler;
    private readonly TeamPlusChannelsOptions _channelsOptions;
    private readonly ILogger<TeamPlusWebhookController> _logger;

    public TeamPlusWebhookController(
        ITeamPlusSignatureVerifier verifier,
        PostbackWebhookHandler handler,
        IOptions<TeamPlusChannelsOptions> channelsOptions,
        ILogger<TeamPlusWebhookController> logger)
    {
        _verifier        = verifier;
        _handler         = handler;
        _channelsOptions = channelsOptions.Value;
        _logger          = logger;
    }

    [HttpPost("postback")]
    [Consumes("application/json")]
    public async Task<IActionResult> ReceivePostback(
        [FromBody] PostbackWebhookBody body,
        CancellationToken ct)
    {
        // 重讀原始 body 用於 HMAC 驗證（需 EnableBuffering）
        Request.Body.Position = 0;
        var rawPayload = await new StreamReader(Request.Body, Encoding.UTF8)
            .ReadToEndAsync(ct);

        var signature = Request.Headers["X-TeamPlus-Signature"].ToString();

        // 依 destination（ChannelId）找對應 event_type
        var eventType = _channelsOptions.Channels
            .FirstOrDefault(kv =>
                string.Equals(kv.Value.ChannelId, body.Destination,
                    StringComparison.OrdinalIgnoreCase))
            .Key;

        bool signatureValid = false;
        if (!string.IsNullOrEmpty(eventType) && !string.IsNullOrEmpty(signature))
            signatureValid = _verifier.Verify(eventType, rawPayload, signature);

        if (!signatureValid)
        {
            _logger.LogWarning(
                "Postback 簽章驗證失敗：Destination={Destination}", body.Destination);
            // 依規格仍回 200（避免 team+ 重送）
        }

        try
        {
            await _handler.HandleAsync(body, rawPayload, signatureValid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Postback 處理發生例外：Destination={Destination}", body.Destination);
        }

        return Ok(new { success = true });
    }
}
