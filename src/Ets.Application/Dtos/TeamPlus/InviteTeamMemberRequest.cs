// src/Ets.Application/Dtos/TeamPlus/InviteTeamMemberRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 邀請加入大團隊請求 DTO（§6.5.1 inviteTeamMember）
/// 用於應變人員回覆 Flex 後動態補拉入大團隊
/// </summary>
/// <param name="TeamSN">EmergencyEvents.TeamPlusBigTeamSN</param>
/// <param name="OperatorAccount">管理員帳號（需具管理權限）</param>
/// <param name="MemberList">欲加入之成員帳號清單</param>
public record InviteTeamMemberRequest(
    long TeamSN,
    string OperatorAccount,
    IReadOnlyList<string> MemberList);
