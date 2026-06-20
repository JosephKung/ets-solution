namespace Ets.Infrastructure.Security;

/// <summary>
/// HIS 端安全設定 Options。
/// 對應 appsettings.json 的 "Security" 區段。
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// HIS 系統允許的 IP 清單（支援單一 IP 及 CIDR 格式）。
    /// 範例：["10.0.1.0/24", "10.0.2.100"]
    /// Dev 環境設為 ["0.0.0.0/0"] 即可全開放。
    /// </summary>
    public List<string> HisAllowedIPs { get; set; } = new();
}
