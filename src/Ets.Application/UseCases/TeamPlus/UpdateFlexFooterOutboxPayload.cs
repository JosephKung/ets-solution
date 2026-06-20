// src/Ets.Application/UseCases/TeamPlus/UpdateFlexFooterOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// UpdateFlexFooter Outbox 任務 Payload（§6.9）
/// 由 Postback Webhook Handler 於使用者回覆後寫入
/// 由 1.3.13 UpdateFlexFooterOutboxHandler 消費
/// </summary>
public sealed record UpdateFlexFooterOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>事件類型 a~e（選取對應服務頻道 AccessToken）</summary>
    string EventType,

    /// <summary>EventResponders.FlexMessageSn</summary>
    int MessageSn,

    /// <summary>收件人帳號（僅更新該人之 Flex footer）</summary>
    string Recipient,

    /// <summary>
    /// Footer 替換文字（§6.9 規格）：
    /// - WillArrive：「已送出！」
    /// - CannotArrive：「已送出！您已表示無法返回。」
    /// - VoiceConfirmed：「語音已送達!請開 team+ 回覆」
    /// - VoiceUnreachable：「語音通報未接通」
    /// </summary>
    string FooterText,

    /// <summary>
    /// Footer 文字顏色：
    /// - #E53935（紅）：WillArrive
    /// - #888888（灰）：CannotArrive / VoiceConfirmed / VoiceUnreachable
    /// </summary>
    string FontColor = "#E53935");
