// src/Ets.Application/UseCases/TeamPlus/AssignManagerOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// AssignTeamManager / AssignChatManager Outbox 任務 Payload
/// 用於事件中期動態提升管理員權限（§6.5.3）
/// MessageType 欄位決定呼叫 assignTeamManager 或 assignChatManager
/// </summary>
public sealed record AssignManagerOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>欲提升為管理員之帳號</summary>
    string ManagerAccount,

    /// <summary>執行操作之管理員帳號（必須已具管理權限）</summary>
    string OperatorAccount,

    /// <summary>
    /// 目標 SN：
    /// - AssignTeamManager → EmergencyEvents.TeamPlusBigTeamSn
    /// - AssignChatManager → EventGroups.TeamPlusChatSn
    /// </summary>
    long TargetSn);
