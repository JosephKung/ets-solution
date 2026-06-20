// src/Ets.Application/Dtos/TeamPlus/InviteChatMemberRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 邀請加入分組交談室請求 DTO（§6.5.2 inviteChatMember）
/// </summary>
/// <param name="ChatSN">EventGroups.TeamPlusChatSN</param>
/// <param name="OperatorAccount">管理員帳號</param>
/// <param name="MemberList">欲加入之成員帳號清單</param>
public record InviteChatMemberRequest(
    long ChatSN,
    string OperatorAccount,
    IReadOnlyList<string> MemberList);
