// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSystemOptions.cs
namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// appsettings.json → "TeamPlusSystem" 區段對應設定
/// </summary>
public sealed class TeamPlusSystemOptions
{
    public const string SectionName = "TeamPlusSystem";

    /// <summary>team+ 內網 Base URL，例如 https://teamplus.hospital.internal</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>system_sn（API 介接來源管理中的系統序號）</summary>
    public string SystemSn { get; init; } = string.Empty;

    /// <summary>api_key（API 介接來源管理中的系統金鑰）</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>HTTP 請求 timeout，預設 10 秒（§6 規格建議）</summary>
    public int TimeoutSeconds { get; init; } = 10;
}
