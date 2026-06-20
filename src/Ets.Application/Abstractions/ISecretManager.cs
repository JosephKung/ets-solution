namespace Ets.Application.Abstractions;

/// <summary>
/// Secret 管理介面。
/// 抽象化 Secret 的存取方式，隔離 Application 層與底層實作（DPAPI / Vault）。
/// 對應規格書 §14.2.5 Secret 管理。
/// </summary>
public interface ISecretManager
{
    /// <summary>
    /// 取得指定名稱的 Secret 明文值。
    /// </summary>
    /// <param name="key">Secret 鍵名（對應 appsettings 路徑，以 ':' 分隔）</param>
    /// <returns>明文值；若不存在則回傳 null</returns>
    string? GetSecret(string key);

    /// <summary>
    /// 取得 Secret，若不存在則拋出例外。
    /// </summary>
    string GetRequiredSecret(string key);
}
