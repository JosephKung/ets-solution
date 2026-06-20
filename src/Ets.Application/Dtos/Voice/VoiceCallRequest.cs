// src/Ets.Application/Dtos/Voice/VoiceCallRequest.cs
namespace Ets.Application.Dtos.Voice;

/// <summary>
/// Voice API 外撥請求 DTO（§9.3）
/// </summary>
/// <param name="EventId">EmergencyEvents.EventId</param>
/// <param name="CalleeAccount">team+ UserNo 字串（§9.2.2 預先解析，非 LoginName）</param>
/// <param name="AudioContent">語音播報文字（EmergencyEvents.AudioContent）</param>
/// <param name="CallbackUrl">ETS Webhook 回呼 URL</param>
/// <param name="RetryCount">本次為第幾次重試（EventResponders.VoiceRetryCount + 1）</param>
public record VoiceCallRequest(
    string EventId,
    string CalleeAccount,
    string AudioContent,
    string CallbackUrl,
    int RetryCount);
