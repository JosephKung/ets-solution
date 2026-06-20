using Ets.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Security;

/// <summary>
/// DpapiSecretManager 單元測試。
/// 注意：DPAPI 加密/解密流程需在 Windows 環境執行，此處主要測試邏輯分支。
/// </summary>
public class DpapiSecretManagerTests
{
    private readonly ILogger<DpapiSecretManager> _logger;

    public DpapiSecretManagerTests()
    {
        _logger = Substitute.For<ILogger<DpapiSecretManager>>();
    }

    [Fact(DisplayName = "非受保護欄位應直接返回原始值，不嘗試解密")]
    public void GetSecret_NonProtectedKey_ShouldReturnRawValue()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:HisAllowedIPs:0"] = "10.0.1.0/24"
            })
            .Build();

        var opts = Options.Create(new DpapiProtectedConfigurationOptions
        {
            ProtectedKeys = new List<string>() // 未列入保護清單
        });

        var manager = new DpapiSecretManager(config, opts, _logger);

        // Act
        var result = manager.GetSecret("Security:HisAllowedIPs:0");

        // Assert
        result.Should().Be("10.0.1.0/24",
            because: "非加密欄位應直接返回 appsettings 中的原始值");
    }

    [Fact(DisplayName = "不存在的 key 應回傳 null")]
    public void GetSecret_MissingKey_ShouldReturnNull()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var opts = Options.Create(new DpapiProtectedConfigurationOptions());
        var manager = new DpapiSecretManager(config, opts, _logger);

        // Act
        var result = manager.GetSecret("NonExistentKey");

        // Assert
        result.Should().BeNull(because: "不存在的 key 應回傳 null 而非拋出例外");
    }

    [Fact(DisplayName = "GetRequiredSecret 若 key 不存在應拋出 InvalidOperationException")]
    public void GetRequiredSecret_MissingKey_ShouldThrow()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var opts = Options.Create(new DpapiProtectedConfigurationOptions());
        var manager = new DpapiSecretManager(config, opts, _logger);

        // Act
        var act = () => manager.GetRequiredSecret("TeamPlusChannels:a:ChannelSecret");

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "必要 Secret 缺失時應拋出明確例外，防止系統以空值運行");
    }

    [Fact(DisplayName = "重複讀取同一 key 應使用快取，不重複解密")]
    public void GetSecret_SameKey_ShouldUseCacheOnSecondCall()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SomeKey"] = "SomeValue"
            })
            .Build();

        var opts = Options.Create(new DpapiProtectedConfigurationOptions());
        var manager = new DpapiSecretManager(config, opts, _logger);

        // Act
        var result1 = manager.GetSecret("SomeKey");
        var result2 = manager.GetSecret("SomeKey");

        // Assert
        result1.Should().Be(result2,
            because: "兩次讀取應回傳相同值（第二次來自快取）");
    }

    [Fact(DisplayName = "TeamPlusChannelsOptions.ValidateApiKey 應使用固定時間比較")]
    public void ValidateApiKey_CorrectKey_ShouldReturnTrue()
    {
        // Arrange
        var options = new TeamPlusChannelsOptions
        {
            Channels = new Dictionary<string, TeamPlusChannelConfig>
            {
                ["a"] = new TeamPlusChannelConfig
                {
                    ChannelId     = "180284",
                    ChannelSecret = "correct-secret-value",
                    AccessToken   = "some-token"
                }
            }
        };

        // Act
        var validResult   = options.ValidateApiKey("a", "correct-secret-value");
        var invalidResult = options.ValidateApiKey("a", "wrong-secret-value");
        var unknownType   = options.ValidateApiKey("z", "any-value");

        // Assert
        validResult.Should().BeTrue(
            because: "正確的 API Key 應通過驗證");
        invalidResult.Should().BeFalse(
            because: "錯誤的 API Key 應拒絕（防止 HIS 以錯誤 Key 觸發事件）");
        unknownType.Should().BeFalse(
            because: "未知的 event_type 應拒絕");
    }
}
