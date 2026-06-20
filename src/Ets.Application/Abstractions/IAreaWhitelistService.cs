namespace Ets.Application.Abstractions;

/// <summary>
/// event_area 白名單驗證介面。
/// 對應規格書 §5.3：防止 HIS 端填入未授權的 event_area。
///
/// 設計特點：
///   - 空白名單 → 不限制模式（任意 event_area 均通過）
///   - 有白名單 → 限制模式（需精確比對）
///   - 實作為 IHostedService，啟動時從加密檔解密載入，全程駐留記憶體
/// </summary>
public interface IAreaWhitelistService
{
    /// <summary>
    /// 驗證指定 event_area 是否在白名單內。
    /// 若白名單為空陣列（不限制模式）則永遠回傳 true。
    /// </summary>
    bool IsAllowed(string? eventArea);

    /// <summary>目前載入的白名單條目（供稽核 / Health Check 使用）</summary>
    IReadOnlyList<string> CurrentWhitelist { get; }

    /// <summary>是否為不限制模式（白名單為空）</summary>
    bool IsUnrestricted { get; }
}
