namespace Ets.Infrastructure.Security;

/// <summary>
/// Area Whitelist 設定 Options。
/// 對應 appsettings.json 的 "Security:AreaWhitelist" 區段。
/// </summary>
public sealed class AreaWhitelistOptions
{
    public const string SectionName = "Security:AreaWhitelist";

    /// <summary>
    /// 加密白名單檔案路徑（.enc）。
    /// 若路徑為空或檔案不存在 → 視為不限制模式（任意 event_area 均通過）。
    /// Production 範例：C:\inetpub\ets\Areas\area_whitelist.enc
    /// </summary>
    public string EncFilePath { get; set; } = string.Empty;
}
