// src/Ets.Application/UseCases/TeamPlus/PostVirtualMsgOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// PostVirtualMsg Outbox 任務 Payload（§6.6.4 postMessage）
/// 由 CreateTeamApiAccountOutboxHandler 完成後插入
/// </summary>
public sealed record PostVirtualMsgOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>大團隊 SN</summary>
    long TeamSn,

    /// <summary>貼文內容（依 Figma A-3 格式組裝）</summary>
    string TextContent,

    /// <summary>貼文主旨</summary>
    string Subject);
