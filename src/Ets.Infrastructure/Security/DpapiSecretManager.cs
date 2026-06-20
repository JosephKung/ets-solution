using Ets.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ets.Infrastructure.Security;

/// <summary>
/// 以 Windows DPAPI 實作的 Secret 管理器。
/// 對應規格書 §14.2.5：敏感欄位（DB 連線字串、ChannelSecret、AccessToken 等）
/// 在 appsettings.Production.json 中以 DPAPI 加密儲存，讀取時自動解密。
///
/// DPAPI 特性：
///   - 加密綁定至加密時的 Windows 使用者帳號（Machine 範圍則綁至機器）
///   - 僅在同一台機器上的相同帳號下可解密
///   - IIS Application Pool Identity 需與加密時的帳號一致
/// </summary>
public sealed class DpapiSecretManager : ISecretManager
{
    private readonly IConfiguration _configuration;
    private readonly DpapiProtectedConfigurationOptions _options;
    private readonly ILogger<DpapiSecretManager> _logger;

    /// <summary>解密值快取，避免每次都重新解密</summary>
    private readonly Dictionary<string, string?> _cache = new();
    private readonly object _lock = new();

    public DpapiSecretManager(
        IConfiguration configuration,
        IOptions<DpapiProtectedConfigurationOptions> options,
        ILogger<DpapiSecretManager> logger)
    {
        _configuration = configuration;
        _options       = options.Value;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public string? GetSecret(string key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var rawValue = _configuration[key];
            if (rawValue is null)
                return null;

            // 判斷是否為 DPAPI 加密值（加密值格式：以 "01000000" 開頭的 hex string）
            var isProtected = _options.ProtectedKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

            if (!isProtected)
            {
                _cache[key] = rawValue;
                return rawValue;
            }

            var decrypted = TryDecrypt(key, rawValue);
            _cache[key] = decrypted;
            return decrypted;
        }
    }

    /// <inheritdoc/>
    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException(
                $"Required secret '{key}' is missing or empty. " +
                "Please ensure the value is set in appsettings and properly encrypted.");
        return value;
    }

    /// <summary>
    /// 嘗試以 DPAPI 解密設定值。
    /// 若解密失敗（機器不符、帳號不符等），記錄錯誤並回傳 null。
    /// </summary>
    private string? TryDecrypt(string key, string encryptedValue)
    {
        try
        {
            // DPAPI 加密值為 Base64 編碼的 SecureString 匯出格式
            // 使用 DataProtectionScope.CurrentUser（與加密時一致）
            var encryptedBytes = Convert.FromBase64String(encryptedValue);
            var decryptedBytes = ProtectedData.Unprotect(
                encryptedData: encryptedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.LocalMachine);

            return Encoding.Unicode.GetString(decryptedBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex,
                "DPAPI 解密失敗：key={Key}。可能原因：機器不符或 App Pool Identity 帳號不一致。",
                key);
            return null;
        }
        catch (FormatException ex)
        {
            // 值不是 Base64 格式 → 可能是明文（非 Production 環境）
            _logger.LogWarning(ex,
                "設定值 '{Key}' 不是有效的 Base64 格式，將直接使用原始值（非加密）", key);
            return encryptedValue;
        }
    }

    /// <summary>
    /// 【靜態工具方法】使用 DPAPI 加密明文 Secret。
    /// 僅供 PowerShell 腳本呼叫或本機開發測試，不應在正式程式流程中呼叫。
    /// </summary>
    /// <param name="plainText">要加密的明文</param>
    /// <returns>Base64 編碼的加密值，可直接寫入 appsettings.Production.json</returns>
    public static string Encrypt(string plainText)
    {
        var plainBytes = Encoding.Unicode.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(
            userData: plainBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encryptedBytes);
    }
}
