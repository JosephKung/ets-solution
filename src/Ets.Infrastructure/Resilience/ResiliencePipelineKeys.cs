namespace Ets.Infrastructure.Resilience;

/// <summary>
/// Polly Resilience Pipeline 識別鍵常數。
/// AddResilienceHandler / ResiliencePipelineProvider 使用同一組鍵，避免魔術字串。
/// </summary>
public static class ResiliencePipelineKeys
{
    /// <summary>team+ API HTTP Client Pipeline</summary>
    public const string TeamPlus = "teamplus-pipeline";

    /// <summary>Voice API HTTP Client Pipeline</summary>
    public const string VoiceApi = "voiceapi-pipeline";
}
