// src/Ets.Application/UseCases/TeamPlus/SendObserverFlexOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// SendObserverFlex Outbox 任務 Payload（§6.4.2）
/// observer 角色收到的是無按鈕版本 Flex（僅通知，無需回覆）
/// 由 M2 EventTriggerHandler 寫入，與 SendFlexMessage 分開處理
/// </summary>
public sealed record SendObserverFlexOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>事件類型 a~e（選取對應服務頻道 AccessToken）</summary>
    string EventType,

    /// <summary>observer 帳號清單（role='observer' 的成員）</summary>
    IReadOnlyList<string> ObserverAccounts);
