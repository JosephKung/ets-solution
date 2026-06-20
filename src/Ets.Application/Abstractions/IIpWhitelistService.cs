namespace Ets.Application.Abstractions;

/// <summary>
/// IP 白名單驗證介面。
/// 對應規格書 §5.1 第三層防護：僅接受院內 HIS 系統 IP 之請求。
/// </summary>
public interface IIpWhitelistService
{
    /// <summary>
    /// 驗證指定 IP 是否在白名單內。
    /// 支援單一 IP（如 "10.0.1.5"）及 CIDR 段（如 "10.0.1.0/24"）。
    /// </summary>
    /// <param name="ipAddress">用戶端 IP 字串（IPv4 或 IPv6）</param>
    /// <returns>true = 允許；false = 拒絕</returns>
    bool IsAllowed(string? ipAddress);
}
