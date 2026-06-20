// src/Ets.Infrastructure/ExternalClients/Voice/VoiceNotifyOptions.cs
namespace Ets.Infrastructure.ExternalClients.Voice;

/// <summary>
/// appsettings.json → "VoiceNotifyConfig" 區段（§9.1）
/// </summary>
public sealed class VoiceNotifyOptions
{
    public const string SectionName = "VoiceNotifyConfig";

    /// <summary>多少分鐘未回覆觸發語音（預設 10 分鐘）</summary>
    public int TimeoutMinutes { get; init; } = 10;

    /// <summary>語音未接最大重試次數（預設 3 次）</summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>Worker 掃描週期（秒，預設 30）</summary>
    public int ScanIntervalSeconds { get; init; } = 30;

    /// <summary>單次掃描處理上限（預設 100 人）</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>語音 API Base URL（例 https://voice.hospital.internal）</summary>
    public string VoiceApiBaseUrl { get; init; } = string.Empty;

    /// <summary>語音 API Bearer Token</summary>
    public string VoiceApiToken { get; init; } = string.Empty;

    /// <summary>ETS Webhook 回呼 URL（帶給 voice API 的 callback_url）</summary>
    public string CallbackBaseUrl { get; init; } = string.Empty;
}
