// src/Ets.Application/Interfaces/External/ITeamPlusSsoClient.cs
using Ets.Application.Dtos.TeamPlus;

namespace Ets.Application.Interfaces.External;

/// <summary>
/// team+ SSO 客戶端介面（system_sn + api_key 認證，對應 WBS 1.3.3）
/// 規格書參照：§10.1 / §10.1.4 / §10.1.5
///
/// 用途：Dashboard 入口 app1.htm 收到 team+ POST 傳入之 session_key 後，
/// 回打 team+ SSOService 換取真實使用者帳號，供後續簽發 ETS JWT。
/// </summary>
public interface ITeamPlusSsoClient
{
    /// <summary>
    /// 以 session_key 換取 team+ 使用者帳號（§10.1.4 getUserAccount）
    /// session_key TTL 為 5 分鐘（team+ 端限制）
    /// </summary>
    Task<SsoUserAccountResult> GetUserAccountAsync(
        string sessionKey,
        CancellationToken ct = default);
}
