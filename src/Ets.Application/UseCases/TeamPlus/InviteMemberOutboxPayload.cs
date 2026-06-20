// src/Ets.Application/UseCases/TeamPlus/InviteMemberOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// InviteTeamMember / InviteChatMember Outbox 任務 Payload
/// 由 Webhook PostbackHandler 於使用者回覆 Flex（intent=WillArrive）後寫入
/// 由 1.3.6 InviteMemberOutboxHandler 消費
///
/// 設計說明：
/// - 同一個 Payload 格式同時服務 InviteTeamMember 和 InviteChatMember
/// - MessageType 欄位決定呼叫哪個 API（InviteTeamMember 或 InviteChatMember）
/// - TargetSN = TeamSN（InviteTeamMember）或 ChatSN（InviteChatMember）
/// </summary>
public sealed record InviteMemberOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>被邀請人帳號</summary>
    string MemberAccount,

    /// <summary>
    /// 操作者帳號（必須具管理員權限）
    /// 使用 event_commander 第一位帳號
    /// </summary>
    string OperatorAccount,

    /// <summary>
    /// 目標 SN：
    /// - InviteTeamMember → EmergencyEvents.TeamPlusBigTeamSn
    /// - InviteChatMember → EventGroups.TeamPlusChatSn
    /// </summary>
    long TargetSn,

    /// <summary>
    /// 分組名稱（InviteChatMember 用，用於查找正確的 EventGroup 更新 JoinedChatRoom）
    /// InviteTeamMember 時可為 null
    /// </summary>
    string? ChatGp = null);
