// src/Ets.Application/UseCases/TeamPlus/CreateTeamApiAccountOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// CreateTeamAPIAccount Outbox 任務 Payload（§6.6.3）
/// 由 CreateTeamOutboxHandler（1.3.4）完成後插入
/// </summary>
public sealed record CreateTeamApiAccountOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>大團隊 SN（EmergencyEvents.TeamPlusBigTeamSn）</summary>
    long TeamSn,

    /// <summary>虛擬帳號擁有者（指揮官帳號）</summary>
    string OwnerAccount);
