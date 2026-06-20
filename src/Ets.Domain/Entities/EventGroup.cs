namespace Ets.Domain.Entities;

/// <summary>
/// 事件分組交談室對照檔。
/// 每筆記錄對應一個 team+ Chat Room（分組交談室）。
/// 對應資料表：EventGroups。
/// </summary>
public class EventGroup
{
    /// <summary>分組識別碼（自動遞增）</summary>
    public long GroupId { get; set; }

    /// <summary>所屬事件識別碼</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 分組交談室名稱（如 "(A0021)消防組"）。
    /// 注意：team+ 限制交談室名稱最多 20 字元，寫入前需截斷。
    /// </summary>
    public string ChatGp { get; set; } = string.Empty;

    /// <summary>分組描述（選填）</summary>
    public string? Description { get; set; }

    /// <summary>team+ Chat SN（Integer，§6.3 createChat 回傳）</summary>
    public int? TeamPlusChatSn { get; set; }

    /// <summary>
    /// 交談室建立者帳號（§6.3 creator_account）。
    /// 後續 inviteChatMember 之 operator 使用。
    /// </summary>
    public string? CreatorAccount { get; set; }

    /// <summary>已加入成員數（追蹤 200 人上限）</summary>
    public int MemberCount { get; set; } = 0;

    /// <summary>
    /// 分流群組序號（§6.3，當單組 > 200 人時分流）。
    /// 第一個分流為 1，第二個為 2，以此類推。
    /// </summary>
    public int? SplitGroupIndex { get; set; }

    /// <summary>建立時間（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    // ── 導航屬性 ──────────────────────────────────────────────────────────

    /// <summary>所屬事件</summary>
    public EmergencyEvent Event { get; set; } = null!;
}
