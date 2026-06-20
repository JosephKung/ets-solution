// src/Ets.Application/Interfaces/External/ITeamPlusSystemClient.cs
using Ets.Application.Dtos.TeamPlus;

namespace Ets.Application.Interfaces.External;

/// <summary>
/// team+ System API 客戶端介面（system_sn + api_key 認證）
/// 負責團隊/交談室 CRUD 及成員管理（對應 WBS 1.3.1）
/// 規格書參照：§6.1.3 / §6.2 / §6.3 / §6.5.1 / §6.5.2 / §6.5.3
/// </summary>
public interface ITeamPlusSystemClient
{
    /// <summary>
    /// 建立緊急處理大團隊（§6.2 createTeam）
    /// 每個 HIS 觸發事件建立一個大團隊，型別固定為封閉式（team_type=1）
    /// </summary>
    Task<CreateTeamResult> CreateTeamAsync(
        CreateTeamRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 建立分組交談室（§6.3 createChat）
    /// 交談室成員上限 200 人，分流邏輯由上層 Use Case 處理（1.3.5）
    /// </summary>
    Task<CreateChatResult> CreateChatAsync(
        CreateChatRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 邀請成員加入大團隊（§6.5.1 inviteTeamMember）
    /// 用於應變人員回覆 Flex 後動態補拉
    /// </summary>
    Task<TeamPlusBaseResult> InviteTeamMemberAsync(
        InviteTeamMemberRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 邀請成員加入分組交談室（§6.5.2 inviteChatMember）
    /// </summary>
    Task<TeamPlusBaseResult> InviteChatMemberAsync(
        InviteChatMemberRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 指派大團隊管理員（§6.5.3 assignTeamManager）
    /// </summary>
    Task<TeamPlusBaseResult> AssignTeamManagerAsync(
        AssignManagerRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 指派交談室管理員（§6.5.3 assignChatManager）
    /// 注意：需確認 team+ 端已啟用「管理者機制」
    /// </summary>
    Task<TeamPlusBaseResult> AssignChatManagerAsync(
        AssignManagerRequest request,
        CancellationToken ct = default);
	/// <summary>
	/// 建立團隊虛擬帳號（§6.6.3 createTeamAPIAccount）
	/// 虛擬帳號用於發佈不可編輯之事件貼文（鎖定編輯機制）
	/// </summary>
	Task<CreateTeamApiAccountResult> CreateTeamApiAccountAsync(
		long teamSn,
		string ownerAccount,
		string accountName,
		CancellationToken ct = default);

	/// <summary>
	/// 以虛擬帳號發佈大團隊互動文章（§6.6.4 postMessage）
	/// 使用 TeamService.ashx（非 SystemService.ashx）
	/// 認證：虛擬帳號 + 虛擬帳號 api_key
	/// </summary>
	Task<PostTeamMessageResult> PostTeamMessageAsync(
		string virtualAccount,
		string virtualApiKey,
		long teamSn,
		string textContent,
		string subject,
		CancellationToken ct = default);
	/// <summary>
	/// 批次解析 LoginName → team+ UserNo（§9.2.2 getUserInfoList）
	/// 每批最多 50 個，自動分批呼叫
	/// 回傳：key = LoginName（大小寫不敏感），value = UserNo
	/// 未找到的 LoginName 不會出現在 Dictionary 中
	/// </summary>
	Task<Dictionary<string, int>> GetUserNosAsync(
		IReadOnlyList<string> loginNames,
		CancellationToken ct = default);
		
}
