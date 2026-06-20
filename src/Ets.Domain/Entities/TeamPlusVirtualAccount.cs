namespace Ets.Domain.Entities;

/// <summary>
/// team+ 虛擬帳號 api_key 加密儲存表。
/// 虛擬帳號用於以系統身分發送 Flex Message（§6.6）。
/// api_key 必須以 AES-256 加密後儲存，記憶體使用後立即清除。
/// 對應資料表：TeamPlusVirtualAccounts。
/// </summary>
public class TeamPlusVirtualAccount
{
    /// <summary>帳號識別碼（自動遞增）</summary>
    public long AccountId { get; set; }

    /// <summary>虛擬帳號識別碼（如 "ets_system_001"）</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>
    /// 虛擬帳號 api_key（AES-256-GCM 加密後儲存）。
    /// 解密金鑰由環境變數注入，不得寫入 appsettings。
    /// </summary>
    public byte[] EncryptedApiKey { get; set; } = Array.Empty<byte>();

    /// <summary>帳號描述（用途說明）</summary>
    public string? Description { get; set; }

    /// <summary>是否啟用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>建立時間（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最後更新時間（UTC）</summary>
    public DateTime UpdatedAt { get; set; }
}
