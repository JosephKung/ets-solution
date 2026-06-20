// src/Ets.Application/Interfaces/ITeamPlusSignatureVerifier.cs
namespace Ets.Application.Interfaces;

/// <summary>
/// team+ Webhook HMAC 簽章驗證介面（§7.1）
/// X-TeamPlus-Signature = Base64(HMAC-SHA256(ChannelSecret, requestBody))
/// </summary>
public interface ITeamPlusSignatureVerifier
{
    /// <summary>
    /// 驗證 X-TeamPlus-Signature header
    /// </summary>
    /// <param name="eventType">事件類型 a~e（用於取得對應 ChannelSecret）</param>
    /// <param name="requestBody">原始 request body 字串（UTF-8）</param>
    /// <param name="signatureHeader">X-TeamPlus-Signature header 值（Base64）</param>
    bool Verify(string eventType, string requestBody, string signatureHeader);
}
