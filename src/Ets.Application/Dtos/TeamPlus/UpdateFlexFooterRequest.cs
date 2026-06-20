// src/Ets.Application/Dtos/TeamPlus/UpdateFlexFooterRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 動態更新 Flex Footer 請求 DTO（§6.9 updateFlexMessageFooter）
/// 應變人員按下按鈕後，將 footer 從「3 按鈕」替換為「已送出！」紅字
/// </summary>
/// <param name="EventType">事件類型，用於選取對應服務頻道 AccessToken</param>
/// <param name="MessageSN">EventResponders.FlexMessageSN</param>
/// <param name="Recipient">個別收件人帳號（僅更新該人之 Flex）</param>
/// <param name="FooterText">替換文字，例如「已送出！」或「已送出！您已表示無法返回。」</param>
/// <param name="FontColor">文字顏色，例如 #E53935（紅）或 #888888（灰）</param>
public record UpdateFlexFooterRequest(
    string EventType,
    int MessageSN,
    string Recipient,
    string FooterText,
    string FontColor = "#E53935");
