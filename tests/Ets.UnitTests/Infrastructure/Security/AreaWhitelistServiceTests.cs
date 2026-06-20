using Ets.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Security;

/// <summary>
/// AreaWhitelistService 單元測試。
/// 覆蓋：不限制模式（未設路徑 / 檔案不存在 / 空白名單）、限制模式、篡改偵測。
/// </summary>
public class AreaWhitelistServiceTests : IDisposable
{
    private readonly ILogger<AreaWhitelistService> _logger;
    private readonly List<string> _tempFiles = new();

    public AreaWhitelistServiceTests()
    {
        _logger = Substitute.For<ILogger<AreaWhitelistService>>();
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────────

    private static AreaWhitelistService Make(string encFilePath)
    {
        var opts = Options.Create(new AreaWhitelistOptions { EncFilePath = encFilePath });
        return new AreaWhitelistService(opts, Substitute.For<ILogger<AreaWhitelistService>>());
    }

    private string CreateEncryptedFile(List<string> areas, byte[] key)
    {
        var dto = new
        {
            event_areaList = areas,
            generated_at   = DateTimeOffset.Now.ToString("o"),
            generated_by   = "test",
            version        = 1
        };
        var json      = JsonSerializer.Serialize(dto);
        var encrypted = AesGcmHelper.Encrypt(json, key);

        var path = Path.GetTempFileName() + ".enc";
        File.WriteAllBytes(path, encrypted);
        _tempFiles.Add(path);
        return path;
    }

    // ── 不限制模式 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "EncFilePath 未設定時應為不限制模式，任何 event_area 通過")]
    public async Task EmptyPath_ShouldBeUnrestricted()
    {
        var svc = Make("");
        await svc.StartAsync(CancellationToken.None);

        svc.IsUnrestricted.Should().BeTrue();
        svc.IsAllowed("任何區域").Should().BeTrue();
    }

    [Fact(DisplayName = "檔案不存在時應為不限制模式")]
    public async Task NonExistentFile_ShouldBeUnrestricted()
    {
        var svc = Make("/non/existent/path.enc");
        await svc.StartAsync(CancellationToken.None);

        svc.IsUnrestricted.Should().BeTrue();
        svc.IsAllowed("林口院區").Should().BeTrue();
    }

    [Fact(DisplayName = "空白名單陣列應為不限制模式")]
    public async Task EmptyWhitelistArray_ShouldBeUnrestricted()
    {
        var key  = AesGcmHelper.GenerateKey();
        var path = CreateEncryptedFile(new List<string>(), key);

        Environment.SetEnvironmentVariable(
            "ETS_AREA_WHITELIST_KEY", Convert.ToBase64String(key));
        try
        {
            var svc = Make(path);
            await svc.StartAsync(CancellationToken.None);

            svc.IsUnrestricted.Should().BeTrue();
            svc.IsAllowed("任何區域").Should().BeTrue();
            svc.CurrentWhitelist.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ETS_AREA_WHITELIST_KEY", null);
        }
    }

    // ── 限制模式 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "有白名單條目時應為限制模式，非白名單 event_area 應被拒絕")]
    public async Task RestrictedMode_ShouldAllowOnly_WhitelistedAreas()
    {
        var key  = AesGcmHelper.GenerateKey();
        var path = CreateEncryptedFile(new List<string> { "林口院區", "台中院區" }, key);

        Environment.SetEnvironmentVariable(
            "ETS_AREA_WHITELIST_KEY", Convert.ToBase64String(key));
        try
        {
            var svc = Make(path);
            await svc.StartAsync(CancellationToken.None);

            svc.IsUnrestricted.Should().BeFalse();
            svc.IsAllowed("林口院區").Should().BeTrue();
            svc.IsAllowed("台中院區").Should().BeTrue();
            svc.IsAllowed("基隆院區").Should().BeFalse(
                because: "基隆院區不在白名單中");
            svc.IsAllowed(null).Should().BeFalse();
            svc.IsAllowed("").Should().BeFalse();
            svc.CurrentWhitelist.Should().HaveCount(2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ETS_AREA_WHITELIST_KEY", null);
        }
    }

    // ── 錯誤處理 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "缺少 ETS_AREA_WHITELIST_KEY 環境變數時應拋出 InvalidOperationException")]
    public async Task MissingEnvKey_ShouldThrow_InvalidOperationException()
    {
        var key  = AesGcmHelper.GenerateKey();
        var path = CreateEncryptedFile(new List<string> { "林口院區" }, key);

        // 確保環境變數不存在
        Environment.SetEnvironmentVariable("ETS_AREA_WHITELIST_KEY", null);

        var svc = Make(path);
        var act = () => svc.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ETS_AREA_WHITELIST_KEY*");
    }

    [Fact(DisplayName = "使用錯誤金鑰解密應拋出 InvalidOperationException（含 CryptographicException）")]
    public async Task WrongKey_ShouldThrow_InvalidOperationException()
    {
        var correctKey = AesGcmHelper.GenerateKey();
        var wrongKey   = AesGcmHelper.GenerateKey();
        var path       = CreateEncryptedFile(new List<string> { "林口院區" }, correctKey);

        Environment.SetEnvironmentVariable(
            "ETS_AREA_WHITELIST_KEY", Convert.ToBase64String(wrongKey));
        try
        {
            var svc = Make(path);
            var act = () => svc.StartAsync(CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*解密失敗*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ETS_AREA_WHITELIST_KEY", null);
        }
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }
}
