// src/Ets.Domain/Enums/OutboxMessageType.cs
namespace Ets.Domain.Enums;

/// <summary>
/// Outbox 訊息類型，對應 §11 定義之操作任務。
/// OutboxDispatcherWorker 依此欄位決定呼叫哪個 handler。
/// </summary>
public enum OutboxMessageType
{
    /// <summary>建立 Big Team（§6.2 createTeam）</summary>
    CreateTeam = 1,

    /// <summary>建立分組交談室（§6.3 createChat）</summary>
    CreateChat = 2,

    /// <summary>加入 Big Team 成員（§6.5.1 inviteTeamMember）</summary>
    InviteTeamMember = 3,

    /// <summary>加入交談室成員（§6.5.2 inviteChatMember）</summary>
    InviteChatMember = 4,

    /// <summary>指派大團隊管理員（§6.5.3 assignTeamManager）</summary>
    AssignTeamManager = 5,

    /// <summary>發送 Flex Message（§6.4，非 observer）</summary>
    SendFlexMessage = 6,

    /// <summary>更新 Flex Footer（§6.9 updateFlexMessageFooter）</summary>
    UpdateFlexFooter = 7,

    /// <summary>建立團隊虛擬帳號（§6.6 createTeamAPIAccount）</summary>
    CreateTeamAPIAccount = 8,

    /// <summary>發佈虛擬帳號貼文（§6.6 postMessage）</summary>
    PostVirtualMsg = 9,

    /// <summary>指派交談室管理員（§6.5.3 assignChatManager）</summary>
    AssignChatManager = 10,

    /// <summary>發送 observer 無按鈕版本 Flex Message（§6.4.2）</summary>
    SendObserverFlexMessage = 11
}
