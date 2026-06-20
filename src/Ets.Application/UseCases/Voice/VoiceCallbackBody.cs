// src/Ets.Application/UseCases/Voice/VoiceCallbackBody.cs
using System.Text.Json.Serialization;

namespace Ets.Application.UseCases.Voice;

/// <summary>
/// Voice API Callback Webhook Request Body（§9.5）
/// voice API 在每個狀態轉換時 POST 至 ETS
/// </summary>
public sealed class VoiceCallbackBody
{
    /// <summary>
    /// 通話唯一識別碼，格式：{EventId}-{UserNo}-{RetryCount}
    /// 第 16 碼（index 15）= event_type，用於 API Key 驗證（§9.5.2）
    /// </summary>
    [JsonPropertyName("external_call_id")]
    public string ExternalCallId { get; init; } = string.Empty;

    /// <summary>
    /// 8 階段狀態之一：
    /// QUEUED → DIALING → RINGING → ANSWERED / REJECTED → PLAYING → PLAY_DONE → COMPLETED
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>通話開始時間（ISO 8601 UTC，可為 null）</summary>
    [JsonPropertyName("started_at")]
    public string? StartedAt { get; init; }

    /// <summary>通話結束時間（ISO 8601 UTC，可為 null）</summary>
    [JsonPropertyName("ended_at")]
    public string? EndedAt { get; init; }

    /// <summary>錯誤碼（失敗時非 null）</summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    /// <summary>錯誤訊息（失敗時非 null）</summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }
}
