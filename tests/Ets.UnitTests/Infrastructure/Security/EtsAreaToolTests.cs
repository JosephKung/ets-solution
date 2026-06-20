using Ets.Infrastructure.Security;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Security;

/// <summary>
/// EtsAreaTool 端對端邏輯測試。
/// 驗證 generate-key → encrypt → inspect 往返流程。
/// （不測試 Console I/O，測試底層加解密邏輯的完整性）
/// </summary>
public class EtsAreaToolTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    // ── 完整往返：產生金鑰 → 加密 → 解密驗證 ───────────────────────────────

    [Fact(DisplayName = "encrypt → inspect 往返應還原原始白名單")]
    public void EncryptInspect_RoundTrip_ShouldRestoreWhitelist()
    {
        // Arrange
        var key   = AesGcmHelper.GenerateKey();
        var areas = new List<string> { "林口院區", "台中院區", "基隆院區" };
        var dto   = new { event_areaList = areas, generated_by = "TestUser", version = 1 };
        var json  = JsonSerializer.Serialize(dto);

        // Act — 加密
        var encrypted = AesGcmHelper.Encrypt(json, key);

        // Act — 解密
        var decrypted = AesGcmHelper.Decrypt(encrypted, key);
        using var doc = JsonDocument.Parse(decrypted);
        var restoredAreas = doc.RootElement
            .GetProperty("event_areaList")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        // Assert
        restoredAreas.Should().BeEquivalentTo(areas,
            because: "加解密往返應還原完整白名單");
    }

    [Fact(DisplayName = "空白名單陣列加密後解密應得到空陣列（不限制模式）")]
    public void EmptyAreaList_RoundTrip_ShouldRestoreEmptyList()
    {
        var key  = AesGcmHelper.GenerateKey();
        var dto  = new { event_areaList = new List<string>(), generated_by = "TestUser", version = 1 };
        var json = JsonSerializer.Serialize(dto);

        var encrypted = AesGcmHelper.Encrypt(json, key);
        var decrypted = AesGcmHelper.Decrypt(encrypted, key);
        using var doc = JsonDocument.Parse(decrypted);
        var areas = doc.RootElement
            .GetProperty("event_areaList")
            .EnumerateArray()
            .ToList();

        areas.Should().BeEmpty(because: "空陣列代表不限制模式");
    }

    [Fact(DisplayName = "產生的金鑰長度應為 32 bytes")]
    public void GenerateKey_ShouldReturn_32ByteKey()
    {
        var key = AesGcmHelper.GenerateKey();
        key.Should().HaveCount(32);
        Convert.ToBase64String(key).Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "ParseKeyFromBase64 應正確解析 generate-key 的輸出")]
    public void ParseKey_FromGenerateKeyOutput_ShouldSucceed()
    {
        // 模擬 generate-key 的輸出
        var originalKey = AesGcmHelper.GenerateKey();
        var base64      = Convert.ToBase64String(originalKey);

        // 模擬使用者複製貼上（含可能的前後空白）
        var parsedKey = AesGcmHelper.ParseKeyFromBase64(base64.Trim());
        parsedKey.Should().Equal(originalKey);
    }

    [Fact(DisplayName = "加密檔案寫入磁碟後讀取應與加密輸出一致")]
    public void EncryptedFile_WrittenAndRead_ShouldBeIdentical()
    {
        // Arrange
        var key   = AesGcmHelper.GenerateKey();
        var json  = "{\"event_areaList\":[\"林口院區\"],\"version\":1}";
        var path  = Path.GetTempFileName() + ".enc";
        _tempFiles.Add(path);

        // Act
        var encrypted = AesGcmHelper.Encrypt(json, key);
        File.WriteAllBytes(path, encrypted);
        var readBack  = File.ReadAllBytes(path);
        var decrypted = AesGcmHelper.Decrypt(readBack, key);

        // Assert
        decrypted.Should().Be(json);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }
}
