using Ets.Application.Abstractions;
using Ets.Application.Commands;
using Ets.Application.Dtos;
using Ets.Infrastructure.Security;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ets.WebApi.Controllers;

/// <summary>
/// HIS 事件觸發 Controller。
/// 路徑：POST /api/v1/his/event-trigger
/// </summary>
[ApiController]
[Route("api/v1/his")]
public sealed class EventTriggerController : ControllerBase
{
    private const string ApiKeyHeader = "X-ETS-API-Key";

    private readonly IMediator _mediator;
    private readonly IValidator<HisEventTriggerDto> _validator;
    private readonly IOptions<TeamPlusChannelsOptions> _channels;
    private readonly IAreaWhitelistService _areaWhitelist;
    private readonly ILogger<EventTriggerController> _logger;

    public EventTriggerController(
        IMediator mediator,
        IValidator<HisEventTriggerDto> validator,
        IOptions<TeamPlusChannelsOptions> channels,
        IAreaWhitelistService areaWhitelist,
        ILogger<EventTriggerController> logger)
    {
        _mediator       = mediator;
        _validator      = validator;
        _channels       = channels;
        _areaWhitelist  = areaWhitelist;
        _logger         = logger;
    }

    /// <summary>HIS 事件觸發入口</summary>
    [HttpPost("event-trigger")]
    [ProducesResponseType(typeof(EtsApiResponse<EventTriggerResponseData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerEvent(
        [FromBody] HisEventTriggerDto dto,
        CancellationToken cancellationToken)
    {
        // ── 步驟 1：FluentValidation ──────────────────────────────────────
        var validationResult = await _validator.ValidateAsync(dto, cancellationToken);
        if (!validationResult.IsValid)
        {
            var firstError = validationResult.Errors.First();
            _logger.LogWarning(
                "HIS 事件觸發驗證失敗：EventId={EventId}, Code={Code}, Msg={Msg}",
                dto.EventId, firstError.ErrorCode, firstError.ErrorMessage);
            return StatusCode(400,
                EtsApiResponse<object>.Fail(firstError.ErrorCode, firstError.ErrorMessage));
        }

        // ── 步驟 2：API Key × event_type 匹配 ────────────────────────────
        var apiKey  = HttpContext.Items[ApiKeyHeader]?.ToString() ?? string.Empty;
        var channel = _channels.Value.GetChannel(dto.EventType);
        if (channel is null)
            return StatusCode(400, EtsApiResponse<object>.Fail(
                "E3003", $"未知的 event_type: '{dto.EventType}'"));

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(apiKey),
                Encoding.UTF8.GetBytes(channel.ChannelSecret)))
        {
            _logger.LogWarning(
                "HIS API Key 不符：EventId={EventId}, EventType={EventType}",
                dto.EventId, dto.EventType);
            return StatusCode(401, EtsApiResponse<object>.Fail(
                "E3010",
                $"X-ETS-API-Key 與 event_type='{dto.EventType}' 對應的 ChannelSecret 不符"));
        }

        // ── 步驟 3.5：event_area 白名單驗證（1.2.7）──────────────────────
        if (!_areaWhitelist.IsAllowed(dto.EventArea))
        {
            _logger.LogWarning(
                "event_area 不在白名單：EventId={EventId}, Area={Area}",
                dto.EventId, dto.EventArea);
            return StatusCode(400, EtsApiResponse<object>.Fail(
                "E3011", $"event_area '{dto.EventArea}' 不在白名單內"));
        }

        // ── 步驟 3~8：發送至 Application 層 ──────────────────────────────
        _logger.LogInformation(
            "HIS 事件觸發：EventId={EventId}, Type={EventType}, Area={Area}",
            dto.EventId, dto.EventType, dto.EventArea);

        var result = await _mediator.Send(new TriggerEventCommand(dto), cancellationToken);

        if (!result.Success)
        {
            var http = result.SuggestedHttpStatus ?? 400;
            return StatusCode(http,
                EtsApiResponse<object>.Fail(result.ErrorCode!, result.ErrorMessage!));
        }

        return Ok(EtsApiResponse<EventTriggerResponseData>.Ok(
            new EventTriggerResponseData(result.EventId!, result.AcceptedAt!.Value),
            "事件觸發成功"));
    }
}

// ── Response Models ────────────────────────────────────────────────────────

public sealed record EventTriggerResponseData(
    [property: System.Text.Json.Serialization.JsonPropertyName("event_id")]
    string EventId,
    [property: System.Text.Json.Serialization.JsonPropertyName("accepted_at")]
    DateTimeOffset AcceptedAt);

public sealed class EtsApiResponse<T>
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public T? Data { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    public static EtsApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Message = message, Data = data };

    public static EtsApiResponse<T> Fail(string errorCode, string errorMessage) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}
