// src/Ets.Application/Dtos/TeamPlus/SsoUserAccountResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// team+ SSO 換取使用者帳號回應 DTO（§10.1.4）
/// </summary>
/// <param name="IsSuccess">是否成功</param>
/// <param name="Description">team+ 回傳說明文字</param>
/// <param name="ErrorCode">
/// team+ 錯誤碼（§10.1.5）：
/// 0=成功 / -1=失敗 / -2=參數有誤 / -3=權限不足 / -4=功能未開放 /
/// -10=查無資訊 / -11=session_key 已過期
/// </param>
/// <param name="UserAccount">team+ 使用者帳號（ErrorCode=0 時有值）</param>
public record SsoUserAccountResult(
    bool IsSuccess,
    string Description,
    int ErrorCode,
    string UserAccount);
