using Ets.Application.Dtos.CheckIn;
using Ets.Application.UseCases.CheckIn;
using Microsoft.AspNetCore.Mvc;

namespace Ets.WebApi.Controllers;

/// <summary>
/// 現場報到 API（§8.5）。
/// team+ App 掃碼後呼叫此端點完成報到。
/// </summary>
[ApiController]
[Route("api/v1/checkin")]
public class CheckInController : ControllerBase
{
    private readonly CheckInUseCase _useCase;
    private readonly ILogger<CheckInController> _logger;

    public CheckInController(
        CheckInUseCase useCase,
        ILogger<CheckInController> logger)
    {
        _useCase = useCase;
        _logger  = logger;
    }

    /// <summary>
    /// POST /api/v1/checkin — QR Code 掃碼報到。
    /// </summary>
    /// <remarks>
    /// 由 team+ App 攔截 Universal Link 後呼叫。
    /// 驗證 QR Token → 更新 CheckInStatus → 補位 Outbox → SignalR 推播。
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(EtsApiResponse<CheckInResponseData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(EtsApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(
        [FromBody] CheckInRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _useCase.ExecuteAsync(request, ct);

            return Ok(EtsApiResponse<CheckInResponseData>.Ok(
                new CheckInResponseData(
                    result.CheckedInAt,
                    result.AddedToTeam,
                    result.AddedToChatRoom,
                    result.ChatRoomName),
                "報到成功，已同步加入應變通訊群組"));
        }
        catch (CheckInException ex)
        {
            _logger.LogWarning("報到業務錯誤。Code={Code} Message={Message}",
                ex.ErrorCode, ex.Message);

            return ex.ErrorCode switch
            {
                // 帳號不在名單 → 202 Accepted（轉臨時申請，1.5.8 完整實作）
                "A7001" => StatusCode(202,
                    EtsApiResponse<object>.Fail("A7001", ex.Message)),

                // 重複報到 → 409
                "C4004" => StatusCode(409,
                    EtsApiResponse<object>.Fail(ex.ErrorCode, ex.Message)),

                // 事件不存在 → 404
                "C4005" => StatusCode(404,
                    EtsApiResponse<object>.Fail(ex.ErrorCode, ex.Message)),

                // Token 過期、簽章無效等 → 400
                _ => StatusCode(400,
                    EtsApiResponse<object>.Fail(ex.ErrorCode, ex.Message))
            };
        }
    }
}

// ── Response Models ────────────────────────────────────────────────────────

/// <summary>報到成功回應 Data 區塊</summary>
public sealed record CheckInResponseData(
    [property: System.Text.Json.Serialization.JsonPropertyName("checked_in_at")]
    DateTime CheckedInAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("added_to_team")]
    bool AddedToTeam,
    [property: System.Text.Json.Serialization.JsonPropertyName("added_to_chat_room")]
    bool AddedToChatRoom,
    [property: System.Text.Json.Serialization.JsonPropertyName("chat_room_name")]
    string? ChatRoomName);
