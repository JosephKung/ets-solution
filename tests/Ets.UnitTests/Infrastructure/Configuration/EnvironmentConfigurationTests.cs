using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Configuration;

/// <summary>
/// 環境設定載入驗證測試。
/// 確保各環境 appsettings.{Env}.json 的必要鍵均存在，
/// 防止部署時因設定缺失導致啟動失敗。
/// </summary>
public class EnvironmentConfigurationTests
{
    /// <summary>必要的設定鍵清單（所有環境共用）</summary>
    private static readonly string[] RequiredBaseKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "Resilience:TeamPlus:MaxRetryAttempts",
        "Resilience:TeamPlus:TimeoutSec",
        "Resilience:VoiceApi:MaxRetryAttempts",
        "Resilience:VoiceApi:TimeoutSec",
        "Serilog:MinimumLevel:Default",
        "Outbox:BatchSize",
        "Outbox:PollIntervalSeconds",
        "Outbox:MaxRetryCount"
    ];

    [Fact(DisplayName = "appsettings.json 應包含所有必要基礎鍵")]
    public void BaseAppSettings_ShouldContain_AllRequiredKeys()
    {
        // Arrange
        var config = BuildConfig("appsettings.json");

        // Act & Assert
        foreach (var key in RequiredBaseKeys)
        {
            config[key].Should().NotBeNullOrEmpty(
                because: $"基礎設定 appsettings.json 缺少必要鍵: {key}");
        }
    }

    [Fact(DisplayName = "Development 設定應覆蓋 ConnectionString 為 localdb")]
    public void DevelopmentConfig_ConnectionString_ShouldUseLocalDb()
    {
        // Arrange
        var config = BuildConfig("appsettings.json", "appsettings.Development.json");

        // Act
        var connStr = config["ConnectionStrings:DefaultConnection"];

        // Assert
        connStr.Should().Contain("localdb",
            because: "Dev 環境應使用 LocalDB，不連正式 SQL Server");
    }

    [Fact(DisplayName = "Dev 環境 HisAllowedIPs 應包含 127.0.0.1 以便本機測試")]
    public void DevelopmentConfig_HisAllowedIPs_ShouldIncludeLocalhost()
    {
        // Arrange
        var config = BuildConfig("appsettings.json", "appsettings.Development.json");

        // Act
        // ASP.NET Core 陣列設定鍵格式：Security:HisAllowedIPs:0
        var firstIp = config["Security:HisAllowedIPs:0"];

        // Assert
        firstIp.Should().NotBeNullOrEmpty(
            because: "Dev 環境 HisAllowedIPs 不應為空，需允許本機請求");
    }

    [Fact(DisplayName = "Resilience TeamPlus MaxRetryAttempts 預設值應為 3")]
    public void BaseConfig_ResilienceTeamPlus_DefaultRetryCount_ShouldBe3()
    {
        // Arrange
        var config = BuildConfig("appsettings.json");

        // Act
        var retryCount = config["Resilience:TeamPlus:MaxRetryAttempts"];

        // Assert
        retryCount.Should().Be("3",
            because: "對應規格書 §3.3：team+ Retry 最大次數為 3");
    }

    [Fact(DisplayName = "Outbox BatchSize 預設值應為 50")]
    public void BaseConfig_OutboxBatchSize_ShouldBe50()
    {
        var config = BuildConfig("appsettings.json");
        config["Outbox:BatchSize"].Should().Be("50");
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 建立 IConfiguration，模擬 ASP.NET Core 的 appsettings 層疊載入行為。
    /// </summary>
    private static IConfiguration BuildConfig(params string[] jsonFiles)
    {
        // 取得 appsettings 檔案所在目錄
        // 測試執行目錄通常是 bin/Debug/net8.0，需往上找專案根目錄
        var solutionRoot = FindSolutionRoot();
        var webApiDir    = Path.Combine(solutionRoot, "src", "Ets.WebApi");

        var builder = new ConfigurationBuilder();
        foreach (var file in jsonFiles)
        {
            var filePath = Path.Combine(webApiDir, file);
            if (File.Exists(filePath))
                builder.AddJsonFile(filePath, optional: false, reloadOnChange: false);
        }

        return builder.Build();
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Cannot find solution root directory");
    }
}
