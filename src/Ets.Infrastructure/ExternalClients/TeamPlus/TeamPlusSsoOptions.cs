// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSsoOptions.cs
namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// appsettings.json → "TeamPlusSso" 區段對應設定（§10.1.4）
/// 使用獨立的 system_sn / api_key（非 TeamPlusSystem 那組）
/// </summary>
public sealed class TeamPlusSsoOptions
{
    public const string SectionName = "TeamPlusSso";

    /// <summary>team+ 內網 Base URL，例如 https://teamplus.hospital.internal</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>SSO 用 system_sn（於 team+ 「系統管理 → API 介接來源管理」取得）</summary>
    public string SystemSn { get; init; } = string.Empty;

    /// <summary>SSO 用 api_key</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>HTTP timeout，預設 10 秒</summary>
    public int TimeoutSeconds { get; init; } = 10;
}
