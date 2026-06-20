namespace Ets.Domain.Entities;

/// <summary>
/// 應變人員動態與通報狀態明細檔。
/// 記錄每位人員在特定事件中的四大狀態（已讀 / 回覆 / 語音 / 報到）。
/// 對應資料表：EventResponders。
/// </summary>
public class EventResponder
{
    /// <summary>應變人員識別碼（自動遞增）</summary>
    public long ResponderId { get; set; }

    /// <summary>所屬事件識別碼</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>team+ 帳號</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>顯示名稱（從 Description 解析）</summary>
    public string? DisplayName { get; set; }

    /// <summary>語音外撥電話號碼</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 原始描述字串（如 "消防組-李小華(0982763543)"）。
    /// 包含姓名與電話，電話顯示時需遮罩。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 人員角色（commander/mgr/vice_mgr/normal/contacts/observer）。
    /// 儲存為字串以對齊 HIS API 原始值。
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 應加入之分組交談室名稱。
    /// 對應 EventGroups.ChatGP 欄位。
    /// </summary>
    public string ChatGp { get; set; } = string.Empty;

    // ── 四大狀態欄位（對應 Dashboard 四欄顯示）────────────────────────────

    /// <summary>已讀狀態（由 §6.8 getMsgReadStatus 推導）</summary>
    public string? ReadStatus { get; set; }

    /// <summary>
    /// 回覆狀態（動態儲存 Flex 按鈕原始文字，§7.2）。
    /// 系統保留值：Pending（尚未回覆）、VoiceConfirmed、VoiceUnreachable。
    /// 其餘為 Flex 按鈕文字（如 "15 分鐘內"、"30 分鐘內"、"無法返回院區"）。
    /// </summary>
    public string ReplyStatus { get; set; } = "Pending";

    /// <summary>
    /// 回覆通道（None / Flex / Voice）。
    /// 用於判斷回覆來源，SSOT 設計原則：Flex 回覆為唯一有效判定。
    /// </summary>
    public string ReplyChannel { get; set; } = "None";

    /// <summary>最新語音通報狀態（v2.9 新增，如 QUEUED/RINGING/COMPLETED/REJECTED）</summary>
    public string? LastVoiceStatus { get; set; }

    /// <summary>最新語音通報狀態時間（v2.9 新增）</summary>
    public DateTime? LastVoiceStatusAt { get; set; }

    /// <summary>現場報到狀態（false=未報到，true=已報到）</summary>
    public bool CheckInStatus { get; set; } = false;

    /// <summary>報到時間</summary>
    public DateTime? CheckInAt { get; set; }

    // ── team+ 整合欄位 ────────────────────────────────────────────────────

    /// <summary>
    /// Flex Message SN（§6.4 postMessage 回傳）。
    /// 用於 §6.8 getMsgReadStatus 及 §6.9 updateFlexMessageFooter。
    /// </summary>
    public int? FlexMessageSn { get; set; }

    // ── 語音外撥欄位 ──────────────────────────────────────────────────────

    /// <summary>語音外撥重試次數</summary>
    public int VoiceRetryCount { get; set; } = 0;

    /// <summary>最後一次語音外撥時間</summary>
    public DateTime? LastVoiceCallTime { get; set; }

    /// <summary>最後一次語音外撥之外部通話 ID（用於 Voice Webhook lookup）</summary>
    public string? LastExternalCallId { get; set; }

    // ── 臨時支援成員 ──────────────────────────────────────────────────────

    /// <summary>是否為臨時支援成員（§8 AdHoc）</summary>
    public bool IsAdHoc { get; set; } = false;

    /// <summary>臨時成員核准人帳號</summary>
    public string? AdHocApprovedBy { get; set; }

    /// <summary>建立時間（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最後更新時間（UTC）</summary>
    public DateTime UpdatedAt { get; set; }

    // ── 導航屬性 ──────────────────────────────────────────────────────────

    /// <summary>所屬事件</summary>
    public EmergencyEvent Event { get; set; } = null!;
	/// <summary>是否已加入大團隊（§6.5.1 inviteTeamMember 成功後設為 true）</summary>
	public bool JoinedTeam { get; set; } = false;

	/// <summary>是否已加入分組交談室（§6.5.2 inviteChatMember 成功後設為 true）</summary>
	public bool JoinedChatRoom { get; set; } = false;
	
	/// <summary>
	/// team+ 內部使用者 ID（§9.2.2 getUserInfoList 回傳之 UserNo）
	/// 於事件建立時批次解析並儲存，語音 fallback 直接讀取（無需即時呼叫 team+ API）
	/// null = 該 LoginName 在 team+ 查無對應（如離職、停用），語音 fallback 將略過該員
	/// </summary>
	public int? UserNo { get; set; }
}
