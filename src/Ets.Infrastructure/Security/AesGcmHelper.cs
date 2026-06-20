using System.Security.Cryptography;
using System.Text;

namespace Ets.Infrastructure.Security;

/// <summary>
/// AES-256-GCM 對稱加解密輔助類別。
/// 對應規格書 §5.4.3。
///
/// 檔案格式（binary）：
///   [Nonce(12 byte)] [Tag(16 byte)] [Ciphertext(N byte)]
///
/// 選擇 AES-GCM 原因：內建完整性驗證 Tag，可防止加密檔被篡改後靜默解出錯誤資料。
/// </summary>
public static class AesGcmHelper
{
    /// <summary>Nonce 長度：96 bit（AES-GCM 標準）</summary>
    public const int NonceSize = 12;

    /// <summary>Tag 長度：128 bit（AES-GCM 最大 tag size）</summary>
    public const int TagSize = 16;

    /// <summary>金鑰長度：256 bit</summary>
    public const int KeySize = 32;

    /// <summary>檔頭最小長度（Nonce + Tag）</summary>
    public const int HeaderSize = NonceSize + TagSize;

    /// <summary>
    /// 加密 UTF-8 字串。
    /// </summary>
    /// <param name="plaintext">明文字串（UTF-8）</param>
    /// <param name="key">32 byte（256 bit）對稱金鑰</param>
    /// <returns>[Nonce(12) | Tag(16) | Ciphertext(N)] 串接之 byte array</returns>
    /// <exception cref="ArgumentException">金鑰長度不為 32 byte</exception>
    public static byte[] Encrypt(string plaintext, byte[] key)
    {
        ValidateKey(key);

        var nonce       = RandomNumberGenerator.GetBytes(NonceSize);
        var tag         = new byte[TagSize];
        var plainBytes  = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        // 組裝輸出：[Nonce | Tag | Cipher]
        var output = new byte[HeaderSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce,       0, output, 0,           NonceSize);
        Buffer.BlockCopy(tag,         0, output, NonceSize,   TagSize);
        Buffer.BlockCopy(cipherBytes, 0, output, HeaderSize,  cipherBytes.Length);
        return output;
    }

    /// <summary>
    /// 解密 byte array 回 UTF-8 字串。
    /// </summary>
    /// <param name="encrypted">[Nonce(12) | Tag(16) | Ciphertext(N)] 串接之 byte array</param>
    /// <param name="key">32 byte（256 bit）對稱金鑰</param>
    /// <returns>解密後之 UTF-8 字串</returns>
    /// <exception cref="ArgumentException">金鑰長度錯誤或資料過短</exception>
    /// <exception cref="CryptographicException">
    /// 解密失敗（金鑰不符或資料被篡改）— GCM Tag 驗證失敗
    /// </exception>
    public static string Decrypt(byte[] encrypted, byte[] key)
    {
        ValidateKey(key);

        if (encrypted.Length < HeaderSize)
            throw new ArgumentException(
                $"Encrypted data too short. Minimum {HeaderSize} bytes required, got {encrypted.Length}.",
                nameof(encrypted));

        var nonce       = new byte[NonceSize];
        var tag         = new byte[TagSize];
        var cipherBytes = new byte[encrypted.Length - HeaderSize];

        Buffer.BlockCopy(encrypted, 0,          nonce,       0, NonceSize);
        Buffer.BlockCopy(encrypted, NonceSize,  tag,         0, TagSize);
        Buffer.BlockCopy(encrypted, HeaderSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 產生新的 256 bit 隨機對稱金鑰。
    /// </summary>
    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySize);

    /// <summary>
    /// 從 Base64 字串解析金鑰並驗證長度。
    /// </summary>
    /// <param name="base64Key">Base64 編碼的 32 byte 金鑰字串</param>
    /// <exception cref="ArgumentException">解碼後長度不為 32 byte</exception>
    public static byte[] ParseKeyFromBase64(string base64Key)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                "金鑰不是合法的 Base64 字串。請確認 ETS_AREA_WHITELIST_KEY 環境變數格式正確。",
                nameof(base64Key), ex);
        }

        ValidateKey(key);
        return key;
    }

    // ── 私有驗證 ─────────────────────────────────────────────────────────────

    private static void ValidateKey(byte[] key)
    {
        if (key is null || key.Length != KeySize)
            throw new ArgumentException(
                $"金鑰長度必須為 {KeySize} bytes（256 bit）。實際長度：{key?.Length ?? 0} bytes。",
                nameof(key));
    }
}
