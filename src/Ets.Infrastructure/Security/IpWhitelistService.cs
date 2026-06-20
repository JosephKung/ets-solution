using System.Net;
using Ets.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ets.Infrastructure.Security;

/// <summary>
/// IP 白名單驗證服務實作。
/// 支援 IPv4 單一位址（"10.0.1.5"）及 CIDR 段（"10.0.1.0/24"）。
/// 白名單在啟動時解析並快取，執行期為 O(N) 比對。
/// 對應規格書 §5.1 第三層防護。
/// </summary>
public sealed class IpWhitelistService : IIpWhitelistService
{
    /// <summary>已解析的白名單規則列表（啟動時一次性建立）</summary>
    private readonly IReadOnlyList<IpRule> _rules;
    private readonly ILogger<IpWhitelistService> _logger;

    public IpWhitelistService(
        IOptions<SecurityOptions> options,
        ILogger<IpWhitelistService> logger)
    {
        _logger = logger;
        _rules  = ParseRules(options.Value.HisAllowedIPs);

        _logger.LogInformation(
            "IpWhitelistService 初始化完成，共 {Count} 條規則",
            _rules.Count);
    }

    /// <inheritdoc/>
    public bool IsAllowed(string? ipAddress)
    {
        // IP 為空或解析失敗 → 拒絕
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        if (!IPAddress.TryParse(ipAddress, out var parsedIp))
        {
            _logger.LogWarning("無法解析 IP 位址：{IpAddress}", ipAddress);
            return false;
        }

        // 白名單為空 → 拒絕所有（安全預設值）
        if (_rules.Count == 0)
        {
            _logger.LogWarning(
                "HIS IP 白名單為空，拒絕來自 {IpAddress} 的請求。請確認 Security:HisAllowedIPs 設定",
                ipAddress);
            return false;
        }

        foreach (var rule in _rules)
        {
            if (rule.Matches(parsedIp))
                return true;
        }

        return false;
    }

    // ── 解析白名單規則 ────────────────────────────────────────────────────────

    private List<IpRule> ParseRules(IEnumerable<string> entries)
    {
        var rules = new List<IpRule>();
        foreach (var entry in entries)
        {
            var trimmed = entry.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.Contains('/'))
            {
                // CIDR 格式（如 "10.0.1.0/24"）
                if (TryParseCidr(trimmed, out var cidrRule))
                    rules.Add(cidrRule!);
                else
                    _logger.LogWarning("無效的 CIDR 規則，已略過：{Entry}", trimmed);
            }
            else
            {
                // 單一 IP（如 "10.0.1.5"）
                if (IPAddress.TryParse(trimmed, out var singleIp))
                    rules.Add(new SingleIpRule(singleIp));
                else
                    _logger.LogWarning("無效的 IP 位址，已略過：{Entry}", trimmed);
            }
        }
        return rules;
    }

    private static bool TryParseCidr(string cidr, out IpRule? rule)
    {
        rule = null;
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;

        if (!IPAddress.TryParse(parts[0], out var networkAddress)) return false;
        if (!int.TryParse(parts[1], out var prefixLength))          return false;

        // IPv4：prefix 0~32；IPv6：0~128
        var maxPrefix = networkAddress.AddressFamily ==
                        System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefix) return false;

        rule = new CidrRule(networkAddress, prefixLength);
        return true;
    }

    // ── IP 規則抽象 ───────────────────────────────────────────────────────────

    private interface IpRule
    {
        bool Matches(IPAddress ip);
    }

    private sealed class SingleIpRule : IpRule
    {
        private readonly IPAddress _ip;
        public SingleIpRule(IPAddress ip) => _ip = ip;
        public bool Matches(IPAddress ip) => _ip.Equals(ip);
    }

    private sealed class CidrRule : IpRule
    {
        private readonly byte[] _networkBytes;
        private readonly byte[] _maskBytes;

        public CidrRule(IPAddress networkAddress, int prefixLength)
        {
            _networkBytes = networkAddress.GetAddressBytes();
            _maskBytes    = BuildMask(_networkBytes.Length, prefixLength);
        }

        public bool Matches(IPAddress ip)
        {
            var ipBytes = ip.GetAddressBytes();
            if (ipBytes.Length != _networkBytes.Length) return false;

            for (int i = 0; i < ipBytes.Length; i++)
            {
                if ((ipBytes[i] & _maskBytes[i]) != (_networkBytes[i] & _maskBytes[i]))
                    return false;
            }
            return true;
        }

        private static byte[] BuildMask(int byteLength, int prefixLength)
        {
            var mask = new byte[byteLength];
            for (int i = 0; i < byteLength; i++)
            {
                if (prefixLength >= 8)
                {
                    mask[i]     = 0xFF;
                    prefixLength -= 8;
                }
                else if (prefixLength > 0)
                {
                    mask[i]     = (byte)(0xFF << (8 - prefixLength));
                    prefixLength = 0;
                }
                else
                {
                    mask[i] = 0x00;
                }
            }
            return mask;
        }
    }
}
