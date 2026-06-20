// src/Ets.Application/Dtos/TeamPlus/GetMsgReadStatusRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 查詢訊息已讀狀態請求 DTO（§6.8 getMsgReadStatus）
/// </summary>
/// <param name="EventType">事件類型，用於選取對應服務頻道 AccessToken</param>
/// <param name="MessageSN">EventResponders.FlexMessageSN</param>
public record GetMsgReadStatusRequest(
    string EventType,
    int MessageSN);
