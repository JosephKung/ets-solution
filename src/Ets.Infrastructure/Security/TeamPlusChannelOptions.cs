using System.ComponentModel.DataAnnotations;

namespace Ets.Infrastructure.Security;

/// <summary>
/// 單一 team+ 服務頻道設定。
/// 對應規格書 §6.1.4：event_type a~e 各對應一個頻道。
/// </summary>
public sealed class TeamPlusChannelConfig
{
    /// <summary>服務頻道 ID（如 "180284"）</summary>
    [Required]
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Channel Secret（雙用途）：
    ///   ① team+ Webhook HMAC 簽章驗證（§7.1）
    ///   ② HIS → ETS X-ETS-API-Key 比對（§5.1）
    /// Production 環境此值為 DPAPI 加密後的 Base64 字串。
    /// </summary>
    [Required]
    public string ChannelSecret { get; set; } = string.Empty;

    /// <summary>
    /// Channel Access Token（Bearer JWT）。
    /// 用於發送 Flex Message、查詢已讀狀態等 Channel API。
    /// Production 環境此值為 DPAPI 加密後的 Base64 字串。
    /// </summary>
    [Required]
    public string AccessToken { get; set; } = string.Empty;
}

/// <summary>
/// team+ 系統 API 設定（System Bot，用於 createTeam / createChat 等）。
/// </summary>
public sealed class TeamPlusSystemOptions
{
    public const string SectionName = "TeamPlusSystem";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>系統代碼（system_sn）</summary>
    [Required]
    public string SystemSn { get; set; } = string.Empty;

    /// <summary>系統 API Key（Production 環境 DPAPI 加密）</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// team+ 所有頻道設定集合（a~e 共 5 組）。
/// 對應 appsettings.json 的 "TeamPlusChannels" 區段。
/// </summary>
public sealed class TeamPlusChannelsOptions
{
    public const string SectionName = "TeamPlusChannels";

    /// <summary>
    /// key = event_type（"a"~"e"），value = 對應頻道設定。
    /// </summary>
    public Dictionary<string, TeamPlusChannelConfig> Channels { get; set; } = new();

    /// <summary>
    /// 依 event_type 取得頻道設定。
    /// </summary>
    public TeamPlusChannelConfig? GetChannel(string eventType)
        => Channels.TryGetValue(eventType.ToLowerInvariant(), out var ch) ? ch : null;

    /// <summary>
    /// 驗證指定 event_type 的 API Key 是否符合。
    /// SSOT：HIS 端帶入的 X-ETS-API-Key = ChannelSecret（§5.1.1）。
    /// </summary>
    public bool ValidateApiKey(string eventType, string apiKey)
    {
        var channel = GetChannel(eventType);
        if (channel is null) return false;

        // 使用固定時間比較，防止 timing attack
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(channel.ChannelSecret),
            System.Text.Encoding.UTF8.GetBytes(apiKey));
    }
}

// 為 ValidateApiKey 引入 System.Security.Cryptography
file static class CryptographicOperations
{
    internal static bool FixedTimeEquals(byte[] a, byte[] b)
        => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
}
