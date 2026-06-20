// src/Ets.Application/Dtos/TeamPlus/CreateTeamResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 建立大團隊回應 DTO（§6.2）
/// </summary>
/// <param name="TeamSN">team+ 回傳之大團隊編號，需回填 EmergencyEvents.TeamPlusBigTeamSN</param>
/// <param name="IgnoredMemberList">未成功加入之成員帳號（需寫 AuditLog）</param>
/// <param name="IgnoredManagerList">未成功指派之管理員帳號（嚴重，需寫 AuditLog）</param>
public record CreateTeamResult(
    bool IsSuccess,
    string Description,
    int ErrorCode,
    long TeamSN,
    IReadOnlyList<string> IgnoredMemberList,
    IReadOnlyList<string> IgnoredManagerList)
    : TeamPlusBaseResult(IsSuccess, Description, ErrorCode);
