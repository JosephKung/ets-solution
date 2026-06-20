// src/Ets.Infrastructure/Security/VirtualAccountKeyEncryptor.cs
using System.Security.Cryptography;
using System.Text;
using Ets.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Security;

/// <summary>
/// 虛擬帳號 ApiKey AES-256-GCM 加解密實作（§6.6 / §12.3）
///
/// 金鑰來源：環境變數 ETS_VIRTUAL_ACCOUNT_KEY（Base64 編碼之 32 bytes）
/// 若未設定，拋出 InvalidOperationException（部署時必須設定）
///
/// 格式（同 AesGcmHelper）：[Nonce(12)] [Tag(16)] [Ciphertext(N)]
/// </summary>
public sealed class VirtualAccountKeyEncryptor : IVirtualAccountKeyEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize   = 16;
    private const int KeySize   = 32;

    /// <summary>環境變數名稱（部署時需設定）</summary>
    public const string EnvKeyName = "ETS_VIRTUAL_ACCOUNT_KEY";

    private readonly byte[] _key;
    private readonly ILogger<VirtualAccountKeyEncryptor> _logger;

    public VirtualAccountKeyEncryptor(ILogger<VirtualAccountKeyEncryptor> logger)
    {
        _logger = logger;

        var keyBase64 = Environment.GetEnvironmentVariable(EnvKeyName)
            ?? throw new InvalidOperationException(
                $"環境變數 {EnvKeyName} 未設定。" +
                $"請以 'openssl rand -base64 32' 產生金鑰並設定至系統環境變數。");

        try
        {
            _key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"環境變數 {EnvKeyName} 格式錯誤，必須為 Base64 編碼。");
        }

        if (_key.Length != KeySize)
            throw new InvalidOperationException(
                $"環境變數 {EnvKeyName} 解碼後長度 {_key.Length} bytes，需為 {KeySize} bytes。");

        _logger.LogDebug("VirtualAccountKeyEncryptor 初始化完成");
    }

    public byte[] Encrypt(string plainApiKey)
    {
        if (string.IsNullOrEmpty(plainApiKey))
            throw new ArgumentException("plainApiKey 不可為空", nameof(plainApiKey));

        var nonce       = RandomNumberGenerator.GetBytes(NonceSize);
        var tag         = new byte[TagSize];
        var plainBytes  = Encoding.UTF8.GetBytes(plainApiKey);
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // [Nonce(12) | Tag(16) | Cipher(N)]
        var output = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce,       0, output, 0,                   NonceSize);
        Buffer.BlockCopy(tag,         0, output, NonceSize,           TagSize);
        Buffer.BlockCopy(cipherBytes, 0, output, NonceSize + TagSize, cipherBytes.Length);
        return output;
    }

    public string Decrypt(byte[] encryptedApiKey)
    {
        if (encryptedApiKey is null || encryptedApiKey.Length < NonceSize + TagSize)
            throw new ArgumentException("encryptedApiKey 長度不足", nameof(encryptedApiKey));

        var nonce       = new byte[NonceSize];
        var tag         = new byte[TagSize];
        var cipherBytes = new byte[encryptedApiKey.Length - NonceSize - TagSize];

        Buffer.BlockCopy(encryptedApiKey, 0,                   nonce,       0, NonceSize);
        Buffer.BlockCopy(encryptedApiKey, NonceSize,           tag,         0, TagSize);
        Buffer.BlockCopy(encryptedApiKey, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using var aes  = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
