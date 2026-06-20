// src/Ets.Application/Dtos/TeamPlus/AssignManagerRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 指派管理員請求 DTO（§6.5.3 assignTeamManager / assignChatManager）
/// 兩個 API 共用此 DTO，由 Client 方法名稱區分
/// </summary>
/// <param name="TeamOrChatSN">大團隊 TeamSN 或交談室 ChatSN</param>
/// <param name="OperatorAccount">執行操作之管理員帳號</param>
/// <param name="ManagerList">欲提升為管理員之帳號清單</param>
public record AssignManagerRequest(
    long TeamOrChatSN,
    string OperatorAccount,
    IReadOnlyList<string> ManagerList);
