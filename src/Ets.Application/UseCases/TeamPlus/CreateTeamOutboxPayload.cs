// src/Ets.Application/UseCases/TeamPlus/CreateTeamOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// CreateTeam Outbox 任務 Payload（序列化後存於 OutboxMessages.PayloadJson）
/// 由 M2 EventTriggerHandler 寫入，由 1.3.4 CreateTeamOutboxHandler 消費
/// </summary>
public sealed record CreateTeamOutboxPayload(
    /// <summary>事件 ID（對應 EmergencyEvents.EventID）</summary>
    string EventId,

    /// <summary>事件類型 a~e（用於選取 ChannelSecret）</summary>
    string EventType,

    /// <summary>指揮官帳號清單（第一位為 Owner）</summary>
    IReadOnlyList<string> CommanderAccounts,

    /// <summary>團隊名稱（已由上游截斷至 50 字）</summary>
    string TeamName,

    /// <summary>主旨（已截斷至 50 字）</summary>
    string Subject,

    /// <summary>事件詳述（已截斷至 300 字）</summary>
    string Description,

    /// <summary>初始成員帳號清單（所有 event_responders.acct）</summary>
    IReadOnlyList<string> MemberAccounts,

    /// <summary>初始管理員帳號清單（event_commander）</summary>
    IReadOnlyList<string> ManagerAccounts);
