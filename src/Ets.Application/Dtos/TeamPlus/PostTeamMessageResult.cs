// src/Ets.Application/Dtos/TeamPlus/PostTeamMessageResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// postMessage 回應 DTO（§6.6.4）
/// </summary>
/// <param name="BatchId">本次發送識別碼，寫入 EmergencyEvents.TeamPlusArticleBatchId</param>
public record PostTeamMessageResult(
    bool IsSuccess,
    string Description,
    int ErrorCode,
    string BatchId)
    : TeamPlusBaseResult(IsSuccess, Description, ErrorCode);
