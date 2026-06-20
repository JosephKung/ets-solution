// src/Ets.Application/Dtos/TeamPlus/BroadcastFlexMessageResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 廣播 Flex Message 回應 DTO（§6.4）
/// </summary>
/// <param name="MessageSN">
/// team+ 回傳之訊息識別碼，必須寫入 EventResponders.FlexMessageSN
/// 後續 §6.8 getMsgReadStatus 及 §6.9 updateFlexMessageFooter 皆需此值
/// </param>
public record BroadcastFlexMessageResult(
    bool IsSuccess,
    int MessageSN);
