// src/Ets.Application/UseCases/TeamPlus/CreateChatOutboxPayload.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// CreateChat Outbox 任務 Payload（序列化後存於 OutboxMessages.PayloadJson）
/// 由 M2 EventTriggerHandler 寫入（每個 EventGroup 一筆），由 1.3.5 CreateChatOutboxHandler 消費
/// </summary>
public sealed record CreateChatOutboxPayload(
    /// <summary>事件 ID</summary>
    string EventId,

    /// <summary>分組 ID（EventGroups.GroupId），用於回填 TeamPlusChatSn</summary>
    long GroupId,

    /// <summary>分組交談室原始名稱，如 "(A0021)消防組"</summary>
    string ChatGp,

    /// <summary>
    /// 建立者帳號（該 chatGP 中 role='mgr' 之首位 acct）
    /// 同時作為 inviteChatMember 之 operator
    /// </summary>
    string CreatorAccount,

    /// <summary>
    /// 初始成員帳號清單（建議僅放 mgr，其餘於回覆/報到後補拉）
    /// </summary>
    IReadOnlyList<string> MemberAccounts,

    /// <summary>初始管理員帳號清單（須包含在 MemberAccounts 中）</summary>
    IReadOnlyList<string> ManagerAccounts,

    /// <summary>
    /// 分流序號（§6.3 200 人分流）
    /// null = 未分流（標準流程）
    /// 1,2,3... = 第 N 個分流交談室
    /// </summary>
    int? SplitGroupIndex = null);
