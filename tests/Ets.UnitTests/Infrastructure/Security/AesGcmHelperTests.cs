using Ets.Infrastructure.Security;
using FluentAssertions;
using System.Security.Cryptography;
using Xunit;

namespace Ets.UnitTests.Infrastructure.Security;

/// <summary>
/// AesGcmHelper 單元測試。
/// 覆蓋：加解密往返、空字串、篡改偵測、金鑰長度驗證、ParseKeyFromBase64。
/// </summary>
public class AesGcmHelperTests
{
    private static byte[] NewKey() => AesGcmHelper.GenerateKey();

    // ── 正常加解密往返 ────────────────────────────────────────────────────────

    [Fact(DisplayName = "加解密往返應還原原始明文")]
    public void EncryptDecrypt_RoundTrip_ShouldRestorePlaintext()
    {
        // Arrange
        var key       = NewKey();
        var plaintext = "林口院區,台中院區,基隆院區";

        // Act
        var encrypted = AesGcmHelper.Encrypt(plaintext, key);
        var decrypted = AesGcmHelper.Decrypt(encrypted, key);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact(DisplayName = "相同明文兩次加密應產生不同密文（每次 Nonce 隨機）")]
    public void Encrypt_SamePlaintext_ShouldProduceDifferentCiphertext()
    {
        var key       = NewKey();
        var plaintext = "test data";

        var enc1 = AesGcmHelper.Encrypt(plaintext, key);
        var enc2 = AesGcmHelper.Encrypt(plaintext, key);

        enc1.Should().NotEqual(enc2,
            because: "每次加密產生新 Nonce，密文不應相同");
    }

    [Fact(DisplayName = "空字串應能正常加解密")]
    public void EncryptDecrypt_EmptyString_ShouldWork()
    {
        var key       = NewKey();
        var encrypted = AesGcmHelper.Encrypt(string.Empty, key);
        var decrypted = AesGcmHelper.Decrypt(encrypted, key);

        decrypted.Should().BeEmpty();
    }

    [Fact(DisplayName = "Unicode 多語系字串應能正確加解密")]
    public void EncryptDecrypt_Unicode_ShouldWork()
    {
        var key       = NewKey();
        var plaintext = "{\"event_areaList\":[\"林口院區\",\"台中院區\"],\"version\":1}";

        var decrypted = AesGcmHelper.Decrypt(AesGcmHelper.Encrypt(plaintext, key), key);
        decrypted.Should().Be(plaintext);
    }

    // ── 加密格式驗證 ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "加密輸出長度應為 HeaderSize + 明文 byte 長度")]
    public void Encrypt_OutputLength_ShouldBeHeaderPlusCiphertext()
    {
        var key       = NewKey();
        var plaintext = "hello";
        var plainLen  = System.Text.Encoding.UTF8.GetByteCount(plaintext);

        var encrypted = AesGcmHelper.Encrypt(plaintext, key);

        encrypted.Length.Should().Be(
            AesGcmHelper.HeaderSize + plainLen,
            because: $"Header={AesGcmHelper.HeaderSize}, Cipher={plainLen}");
    }

    // ── 篡改偵測（GCM Tag 驗證）──────────────────────────────────────────────

    [Fact(DisplayName = "篡改密文中任一 byte 應觸發 CryptographicException")]
    public void Tampered_Ciphertext_ShouldThrow_CryptographicException()
    {
        var key       = NewKey();
        var encrypted = AesGcmHelper.Encrypt("sensitive data", key);

        // 篡改最後一個 byte（ciphertext 部分）
        var tampered = (byte[])encrypted.Clone();
        tampered[^1] ^= 0xFF;

        var act = () => AesGcmHelper.Decrypt(tampered, key);
        act.Should().Throw<CryptographicException>(
            because: "GCM Tag 驗證失敗，應拋出 CryptographicException");
    }

    [Fact(DisplayName = "使用錯誤金鑰解密應觸發 CryptographicException")]
    public void WrongKey_ShouldThrow_CryptographicException()
    {
        var key1      = NewKey();
        var key2      = NewKey();
        var encrypted = AesGcmHelper.Encrypt("secret", key1);

        var act = () => AesGcmHelper.Decrypt(encrypted, key2);
        act.Should().Throw<CryptographicException>();
    }

    // ── 金鑰驗證 ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "金鑰長度不為 32 byte 應拋出 ArgumentException")]
    [InlineData(16)]   // AES-128
    [InlineData(24)]   // AES-192
    [InlineData(31)]
    [InlineData(33)]
    public void InvalidKeyLength_ShouldThrow_ArgumentException(int keyLength)
    {
        var shortKey = new byte[keyLength];

        var encAct = () => AesGcmHelper.Encrypt("test", shortKey);
        var decAct = () => AesGcmHelper.Decrypt(new byte[AesGcmHelper.HeaderSize + 1], shortKey);

        encAct.Should().Throw<ArgumentException>();
        decAct.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "資料過短（< 28 byte）應拋出 ArgumentException")]
    public void TooShortData_ShouldThrow_ArgumentException()
    {
        var key = NewKey();
        var act = () => AesGcmHelper.Decrypt(new byte[10], key);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*28*");
    }

    // ── ParseKeyFromBase64 ────────────────────────────────────────────────────

    [Fact(DisplayName = "ParseKeyFromBase64 應正確解析 Base64 金鑰")]
    public void ParseKeyFromBase64_ValidBase64_ShouldReturnKey()
    {
        var originalKey = NewKey();
        var base64      = Convert.ToBase64String(originalKey);

        var parsedKey = AesGcmHelper.ParseKeyFromBase64(base64);
        parsedKey.Should().Equal(originalKey);
    }

    [Fact(DisplayName = "ParseKeyFromBase64 傳入無效 Base64 應拋出 ArgumentException")]
    public void ParseKeyFromBase64_InvalidBase64_ShouldThrow()
    {
        var act = () => AesGcmHelper.ParseKeyFromBase64("not-valid-base64!!");
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "ParseKeyFromBase64 解碼後長度不為 32 應拋出 ArgumentException")]
    public void ParseKeyFromBase64_WrongLength_ShouldThrow()
    {
        var shortKey = Convert.ToBase64String(new byte[16]); // AES-128，非 256
        var act      = () => AesGcmHelper.ParseKeyFromBase64(shortKey);
        act.Should().Throw<ArgumentException>();
    }

    // ── GenerateKey ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "GenerateKey 應產生 32 byte 的隨機金鑰")]
    public void GenerateKey_ShouldReturn_32ByteKey()
    {
        var key = AesGcmHelper.GenerateKey();
        key.Should().HaveCount(32);
    }

    [Fact(DisplayName = "兩次 GenerateKey 應產生不同金鑰")]
    public void GenerateKey_CalledTwice_ShouldProduceDifferentKeys()
    {
        var k1 = AesGcmHelper.GenerateKey();
        var k2 = AesGcmHelper.GenerateKey();
        k1.Should().NotEqual(k2);
    }
}
