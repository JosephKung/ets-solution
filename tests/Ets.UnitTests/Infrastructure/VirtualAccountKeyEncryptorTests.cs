// tests/Ets.UnitTests/Infrastructure/VirtualAccountKeyEncryptorTests.cs
using System.Security.Cryptography;
using Ets.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ets.UnitTests.Infrastructure;

public sealed class VirtualAccountKeyEncryptorTests : IDisposable
{
    private readonly string _originalEnvValue;

    public VirtualAccountKeyEncryptorTests()
    {
        // 設定測試用金鑰（32 bytes random）
        _originalEnvValue = Environment.GetEnvironmentVariable(
            VirtualAccountKeyEncryptor.EnvKeyName) ?? string.Empty;

        var testKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable(VirtualAccountKeyEncryptor.EnvKeyName, testKey);
    }

    public void Dispose()
    {
        // 還原環境變數
        Environment.SetEnvironmentVariable(
            VirtualAccountKeyEncryptor.EnvKeyName,
            string.IsNullOrEmpty(_originalEnvValue) ? null : _originalEnvValue);
    }

    // ─── 測試 1：加密後解密應還原原始值 ──────────────────────────
    [Fact]
    public void EncryptDecrypt_應正確還原原始ApiKey()
    {
        var encryptor  = new VirtualAccountKeyEncryptor(
            NullLogger<VirtualAccountKeyEncryptor>.Instance);
        var plainApiKey = "9d12abef-aaaa-bbbb-cccc-1234567890ab";

        var encrypted = encryptor.Encrypt(plainApiKey);
        var decrypted = encryptor.Decrypt(encrypted);

        decrypted.Should().Be(plainApiKey);
    }

    // ─── 測試 2：每次加密結果不同（Nonce 隨機）─────────────────
    [Fact]
    public void Encrypt_每次結果應不同()
    {
        var encryptor   = new VirtualAccountKeyEncryptor(
            NullLogger<VirtualAccountKeyEncryptor>.Instance);
        var plainApiKey = "test-api-key";

        var encrypted1 = encryptor.Encrypt(plainApiKey);
        var encrypted2 = encryptor.Encrypt(plainApiKey);

        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    // ─── 測試 3：加密後長度應大於明文長度 ────────────────────────
    [Fact]
    public void Encrypt_加密後長度應至少多28Bytes()
    {
        var encryptor   = new VirtualAccountKeyEncryptor(
            NullLogger<VirtualAccountKeyEncryptor>.Instance);
        var plainApiKey = "short-key";

        var encrypted = encryptor.Encrypt(plainApiKey);

        // Nonce(12) + Tag(16) = 28 bytes overhead
        encrypted.Length.Should().BeGreaterThan(plainApiKey.Length);
        encrypted.Length.Should().Be(plainApiKey.Length + 28);
    }

    // ─── 測試 4：環境變數未設定應拋出例外 ────────────────────────
    [Fact]
    public void Constructor_環境變數未設定_應拋出InvalidOperationException()
    {
        Environment.SetEnvironmentVariable(VirtualAccountKeyEncryptor.EnvKeyName, null);

        var act = () => new VirtualAccountKeyEncryptor(
            NullLogger<VirtualAccountKeyEncryptor>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{VirtualAccountKeyEncryptor.EnvKeyName}*");
    }

    // ─── 測試 5：竄改密文應拋出 CryptographicException ───────────
    [Fact]
    public void Decrypt_竄改密文_應拋出CryptographicException()
    {
        var encryptor = new VirtualAccountKeyEncryptor(
            NullLogger<VirtualAccountKeyEncryptor>.Instance);

        var encrypted = encryptor.Encrypt("original-key");

        // 竄改最後一個 byte
        encrypted[^1] ^= 0xFF;

        var act = () => encryptor.Decrypt(encrypted);
        act.Should().Throw<CryptographicException>();
    }
}
