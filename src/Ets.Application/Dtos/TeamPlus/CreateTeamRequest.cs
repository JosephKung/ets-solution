// src/Ets.Application/Dtos/TeamPlus/CreateTeamRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 建立大團隊請求 DTO（對應規格書 §6.2 createTeam）
/// </summary>
/// <param name="Owner">指揮官帳號（event_commander 首位）</param>
/// <param name="Name">團隊名稱，最長 50 字（超出自動截斷）</param>
/// <param name="Subject">主旨，最長 50 字</param>
/// <param name="Description">事件詳述，最長 300 字</param>
/// <param name="MemberList">初始成員帳號清單</param>
/// <param name="ManagerList">初始管理員帳號清單</param>
public record CreateTeamRequest(
    string Owner,
    string Name,
    string Subject,
    string Description,
    IReadOnlyList<string> MemberList,
    IReadOnlyList<string> ManagerList);
