// src/Ets.Application/Interfaces/IVoiceApiClient.cs
using Ets.Application.Dtos.Voice;

namespace Ets.Application.Interfaces;

/// <summary>
/// Voice API 外撥客戶端介面（§9.3）
/// </summary>
public interface IVoiceApiClient
{
    /// <summary>
    /// 發起語音外撥（§9.3 POST /api/v1/voice-call）
    /// </summary>
    Task<VoiceCallResult> CallAsync(VoiceCallRequest request, CancellationToken ct = default);
}

