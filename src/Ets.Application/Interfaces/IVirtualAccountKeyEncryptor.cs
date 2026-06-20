// src/Ets.Application/Interfaces/IVirtualAccountKeyEncryptor.cs
namespace Ets.Application.Interfaces;

/// <summary>
/// 虛擬帳號 ApiKey 加解密介面（§6.6 §12.3）
/// 使用 AES-256-GCM，金鑰來自環境變數 ETS_VIRTUAL_ACCOUNT_KEY
/// 加密後以 byte[] 寫入 EmergencyEvents.TeamPlusVirtualAccountApiKey（VARBINARY）
/// </summary>
public interface IVirtualAccountKeyEncryptor
{
    /// <summary>加密明文 ApiKey → byte[]（寫入 DB）</summary>
    byte[] Encrypt(string plainApiKey);

    /// <summary>解密 byte[] → 明文 ApiKey（供 PostTeamMessageAsync 使用）</summary>
    string Decrypt(byte[] encryptedApiKey);
}
