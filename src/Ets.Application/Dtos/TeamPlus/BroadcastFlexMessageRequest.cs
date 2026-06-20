// src/Ets.Application/Dtos/TeamPlus/BroadcastFlexMessageRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 廣播 Flex Message 請求 DTO（§6.4 broadcastMessageByLoginNameList）
/// 注意：Channel Client 負責組裝最終 JSON Body，此 DTO 為 Application 層傳入參數
/// </summary>
/// <param name="EventType">事件類型 a~e，用於選取對應服務頻道 AccessToken</param>
/// <param name="RecipientList">收件人帳號清單</param>
/// <param name="FlexContents">完整 Flex Message contents 物件（由 FlexMessageBuilder 組裝）</param>
public record BroadcastFlexMessageRequest(
    string EventType,
    IReadOnlyList<string> RecipientList,
    object FlexContents);
