using Ets.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Security;

/// <summary>
/// IpWhitelistService 單元測試。
/// 覆蓋：單一 IP、CIDR 段、空白名單、無效格式、X-Forwarded-For。
/// </summary>
public class IpWhitelistServiceTests
{
    private static IpWhitelistService Make(params string[] allowedIps)
    {
        var opts   = Options.Create(new SecurityOptions { HisAllowedIPs = allowedIps.ToList() });
        var logger = Substitute.For<ILogger<IpWhitelistService>>();
        return new IpWhitelistService(opts, logger);
    }

    [Theory(DisplayName = "單一 IP 精確匹配應通過")]
    [InlineData("10.0.1.5",  "10.0.1.5",  true)]
    [InlineData("10.0.1.5",  "10.0.1.6",  false)]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    public void SingleIp_Matching(string allowedIp, string clientIp, bool expected)
    {
        var svc = Make(allowedIp);
        svc.IsAllowed(clientIp).Should().Be(expected);
    }

    [Theory(DisplayName = "CIDR /24 段應正確判斷")]
    [InlineData("10.0.1.0/24", "10.0.1.1",   true)]
    [InlineData("10.0.1.0/24", "10.0.1.254", true)]
    [InlineData("10.0.1.0/24", "10.0.2.1",   false)]
    [InlineData("10.0.0.0/8",  "10.255.1.1", true)]
    [InlineData("10.0.0.0/8",  "11.0.0.1",   false)]
    public void CidrRange_Matching(string cidr, string clientIp, bool expected)
    {
        var svc = Make(cidr);
        svc.IsAllowed(clientIp).Should().Be(expected);
    }

    [Fact(DisplayName = "全開放 0.0.0.0/0 應允許任何 IPv4")]
    public void AllowAll_ShouldAllow_AnyIpv4()
    {
        var svc = Make("0.0.0.0/0");
        svc.IsAllowed("8.8.8.8").Should().BeTrue();
        svc.IsAllowed("192.168.1.100").Should().BeTrue();
    }

    [Fact(DisplayName = "白名單為空時應拒絕所有請求（安全預設）")]
    public void EmptyWhitelist_ShouldDenyAll()
    {
        var svc = Make(); // 空白名單
        svc.IsAllowed("10.0.1.5").Should().BeFalse(
            because: "空白名單是安全預設值，應拒絕所有 IP");
    }

    [Theory(DisplayName = "null 或空字串 IP 應拒絕")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmpty_Ip_ShouldDeny(string? ip)
    {
        var svc = Make("10.0.1.0/24");
        svc.IsAllowed(ip).Should().BeFalse();
    }

    [Fact(DisplayName = "無效 IP 格式應拒絕（不拋出例外）")]
    public void InvalidIpFormat_ShouldDeny_WithoutThrowing()
    {
        var svc = Make("10.0.1.0/24");
        var act = () => svc.IsAllowed("not-an-ip");
        act.Should().NotThrow();
        svc.IsAllowed("not-an-ip").Should().BeFalse();
    }

    [Fact(DisplayName = "多條規則混合（單一 IP + CIDR）應正確判斷")]
    public void MultipleRules_MixedTypes_ShouldWork()
    {
        var svc = Make("10.0.1.0/24", "172.16.5.10");

        svc.IsAllowed("10.0.1.100").Should().BeTrue(because: "符合 CIDR 規則");
        svc.IsAllowed("172.16.5.10").Should().BeTrue(because: "符合單一 IP 規則");
        svc.IsAllowed("172.16.5.11").Should().BeFalse(because: "不符合任何規則");
        svc.IsAllowed("8.8.8.8").Should().BeFalse(because: "不符合任何規則");
    }
}
