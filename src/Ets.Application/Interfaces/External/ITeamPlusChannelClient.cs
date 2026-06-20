// src/Ets.Application/Interfaces/External/ITeamPlusChannelClient.cs
using Ets.Application.Dtos.TeamPlus;

namespace Ets.Application.Interfaces.External;

/// <summary>
/// team+ Channel API 客戶端介面（Channel Access Token Bearer 認證）
/// 負責 Flex Message 推送、已讀查詢、Footer 更新（對應 WBS 1.3.2）
/// 規格書參照：§6.1.3 / §6.4 / §6.8 / §6.9
///
/// 與 ITeamPlusSystemClient 的差異：
/// - 認證方式：Authorization: Bearer {AccessToken}（非 system_sn/api_key）
/// - 傳輸格式：application/json（非 form-urlencoded）
/// - Endpoint：MessageFeedService.ashx（非 SystemService.ashx）
/// - 多頻道：依 event_type 選取對應的 ChannelId / AccessToken（TeamPlusChannelsOptions）
/// </summary>
public interface ITeamPlusChannelClient
{
    /// <summary>
    /// 廣播 Flex Message 至指定帳號清單（§6.4 broadcastMessageByLoginNameList）
    /// 回傳 MessageSN 必須寫入 EventResponders.FlexMessageSN
    /// </summary>
    Task<BroadcastFlexMessageResult> BroadcastFlexMessageAsync(
        BroadcastFlexMessageRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 查詢訊息已讀狀態（§6.8 getMsgReadStatus）
    /// 每 30 秒輪詢一次，用於 Dashboard 已讀統計
    /// </summary>
    Task<GetMsgReadStatusResult> GetMsgReadStatusAsync(
        GetMsgReadStatusRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 動態更新 Flex Footer（§6.9 updateFlexMessageFooter）
    /// 應變人員按下 PostbackButton 後，將 footer 替換為「已送出！」
    /// </summary>
    Task<TeamPlusBaseResult> UpdateFlexFooterAsync(
        UpdateFlexFooterRequest request,
        CancellationToken ct = default);
}
