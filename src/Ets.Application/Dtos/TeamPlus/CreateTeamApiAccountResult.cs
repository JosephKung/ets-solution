// src/Ets.Application/Dtos/TeamPlus/CreateTeamApiAccountResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// createTeamAPIAccount 回應 DTO（§6.6.3）
/// </summary>
/// <param name="IsSuccess">是否成功</param>
/// <param name="ApiAccount">虛擬帳號識別碼（後續 postMessage 之 account 參數）</param>
/// <param name="ApiKey">虛擬帳號金鑰（後續 postMessage 之 api_key 參數，需加密儲存）</param>
public record CreateTeamApiAccountResult(
    bool IsSuccess,
    string Description,
    int ErrorCode,
    string ApiAccount,
    string ApiKey)
    : TeamPlusBaseResult(IsSuccess, Description, ErrorCode);
