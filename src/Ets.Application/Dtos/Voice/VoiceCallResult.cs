// src/Ets.Application/Dtos/Voice/VoiceCallResult.cs
namespace Ets.Application.Dtos.Voice;

/// <summary>
/// Voice API 外撥回應 DTO（§9.3）
/// </summary>
/// <param name="ExternalCallId">
/// 唯一識別碼，格式 {EventId}-{UserNo}-{RetryCount}
/// 需寫入 EventResponders.LastExternalCallId
/// 後續 Voice Callback Webhook 帶此值回呼（§9.5）
/// </param>
/// <param name="Status">目前固定為 "QUEUED"</param>
/// <param name="QueueInUse">語音 API 進行中通話數</param>
/// <param name="QueueMax">語音 API 最大並發數</param>
/// <param name="QueueWaiting">語音 API 等待中通話數</param>
public record VoiceCallResult(
    bool IsSuccess,
    string ExternalCallId,
    string Status,
    int QueueInUse,
    int QueueMax,
    int QueueWaiting);
