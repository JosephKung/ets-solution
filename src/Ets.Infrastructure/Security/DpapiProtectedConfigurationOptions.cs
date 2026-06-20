namespace Ets.Infrastructure.Security;

/// <summary>
/// DPAPI 加密設定 Options。
/// 從 appsettings.json 的 "DpapiProtectedFields" 區段載入，
/// 列出哪些 appsettings 欄位是 DPAPI 加密值，需要在讀取時解密。
///
/// 對應規格書 §14.2.5：
///   DB 連線字串、TeamPlus ChannelSecret/AccessToken、JWT Key 等敏感欄位
///   於 appsettings.Production.json 中以 DPAPI 加密後儲存。
/// </summary>
public sealed class DpapiProtectedConfigurationOptions
{
    public const string SectionName = "DpapiProtectedFields";

    /// <summary>
    /// 需要 DPAPI 解密的設定路徑清單。
    /// 值為 appsettings 中的 key path（以 ':' 分隔）。
    /// 範例：["ConnectionStrings:DefaultConnection", "TeamPlusChannels:a:ChannelSecret"]
    /// </summary>
    public List<string> ProtectedKeys { get; set; } = new();
}
