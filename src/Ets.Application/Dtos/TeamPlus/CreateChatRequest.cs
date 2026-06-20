// src/Ets.Application/Dtos/TeamPlus/CreateChatRequest.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 建立分組交談室請求 DTO（對應規格書 §6.3 createChat）
/// 注意：交談室成員上限 200 人，超出需分流（§6.3 分流邏輯由 1.3.5 處理）
/// </summary>
/// <param name="CreatorAccount">建立者帳號（chatGP 中 role='mgr' 之首位）</param>
/// <param name="ChatName">交談室名稱，最長 20 字（超出自動截斷）</param>
/// <param name="MemberList">初始成員帳號清單</param>
/// <param name="ManagerList">初始管理員帳號清單（須包含在 MemberList 中）</param>
public record CreateChatRequest(
    string CreatorAccount,
    string ChatName,
    IReadOnlyList<string> MemberList,
    IReadOnlyList<string> ManagerList);
