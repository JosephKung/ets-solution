namespace Ets.Application.Interfaces;

/// <summary>
/// QR HMAC 金鑰提供者介面。
/// 實作位於 Infrastructure，從設定或環境變數取得金鑰。
/// </summary>
public interface IQrHmacKeyProvider
{
    /// <summary>取得 HMAC-SHA256 簽章金鑰字串</summary>
    string GetKey();
}
