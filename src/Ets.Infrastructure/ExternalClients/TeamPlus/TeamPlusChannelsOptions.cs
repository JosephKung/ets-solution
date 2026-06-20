// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusChannelsOptions.cs
namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// appsettings.json → "TeamPlusChannels" 區段對應設定
/// 事件類型 a~e 各自對應一組服務頻道設定（§6.1.4）
/// </summary>
public sealed class TeamPlusChannelsOptions
{
    public const string SectionName = "TeamPlusChannels";

    /// <summary>key = event_type（a/b/c/d/e），value = 該頻道設定</summary>
    public Dictionary<string, TeamPlusChannelEntry> Channels { get; init; } = new();
}

/// <summary>單一服務頻道設定</summary>
public sealed class TeamPlusChannelEntry
{
    /// <summary>服務頻道代碼（Channel ID），例如 "180284"</summary>
    public string ChannelId { get; init; } = string.Empty;

    /// <summary>Channel Secret（用於 Webhook 簽章驗證 §7.1 及 HIS API Key §5.1）</summary>
    public string ChannelSecret { get; init; } = string.Empty;

    /// <summary>Channel Access Token（Bearer JWT，用於 Flex Message API）</summary>
    public string AccessToken { get; init; } = string.Empty;
}
