using Ets.Domain.Enums;

namespace Ets.Domain.Entities;

/// <summary>
/// 緊急應變事件主檔。
/// 由 HIS 系統觸發，為整個應變流程之根聚合。
/// 對應資料表：EmergencyEvents。
/// </summary>
public class EmergencyEvent
{
    /// <summary>
    /// 事件唯一識別碼。
    /// 格式：E + YYYYMMDDHHMMSS + event_type(1碼) + 3碼流水號
    /// 範例：E20240101120000A001（第 16 碼 'A' = event_type）
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>事件類型（a~e，對應不同 team+ 服務頻道）</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>HIS 觸發時間</summary>
    public DateTime EventTime { get; set; }

    /// <summary>事件摘要（作為 Flex Message 卡片標題）</summary>
    public string EventSummary { get; set; } = string.Empty;

    /// <summary>事件詳細描述（選填）</summary>
    public string? EventDescription { get; set; }

    /// <summary>
    /// 應變區域名稱（v2.7 通用化命名）。
    /// 必須通過 §5.3 加密白名單驗證，否則拒絕事件觸發。
    /// </summary>
    public string? EventArea { get; set; }

    /// <summary>語音播報文字（傳給語音 API 之 AudioContent）</summary>
    public string? AudioContent { get; set; }

    /// <summary>
    /// 音檔名稱（v2.8 已棄用，保留以向下相容）。
    /// 音檔由語音 API 內部管理，ETS 不再追蹤。
    /// </summary>
    [Obsolete("v2.8 已棄用，音檔由語音 API 管理")]
    public string? AudioFileName { get; set; }

    /// <summary>事件來源（預設 'HIS'）</summary>
    public string EventSource { get; set; } = "HIS";

    /// <summary>Flex Message 按鈕項目 JSON 陣列字串（原始值）</summary>
    public string? FlexMsgItemsJson { get; set; }

    /// <summary>Flex Message 按鈕語意映射 JSON（§7.2.1 自動推導結果）</summary>
    public string? FlexMsgIntentMapJson { get; set; }

    /// <summary>事件狀態</summary>
    public EventStatus Status { get; set; } = EventStatus.Processing;

    // ── team+ 整合欄位（§6.2 createTeam 回傳）────────────────────────────

    /// <summary>team+ Big Team SN（Integer，§6.2 createTeam 回傳）</summary>
    public int? TeamPlusBigTeamSn { get; set; }

    /// <summary>team+ Channel ID（如 T_99823104，§6.2 createTeam 回傳）</summary>
    public string? TeamPlusChannelId { get; set; }

    /// <summary>虛擬帳號識別碼（§6.6）</summary>
    public string? TeamPlusVirtualAccount { get; set; }

    /// <summary>虛擬帳號 api_key（AES-256 加密後儲存，§6.6）</summary>
    public byte[]? TeamPlusVirtualAccountApiKey { get; set; }

    /// <summary>postMessage 之 BatchID（§6.6）</summary>
    public string? TeamPlusArticleBatchId { get; set; }

    // ── 已讀數快取（§6.8，每 30 秒刷新）────────────────────────────────

    /// <summary>已讀人數快取值</summary>
    public int? LastReadCount { get; set; }

    /// <summary>已讀人數最後更新時間</summary>
    public DateTime? LastReadCountFetchAt { get; set; }

    /// <summary>結案時間</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>建立時間（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最後更新時間（UTC）</summary>
    public DateTime UpdatedAt { get; set; }

    // ── 導航屬性 ──────────────────────────────────────────────────────────

    /// <summary>事件分組交談室清單</summary>
    public ICollection<EventGroup> Groups { get; set; } = new List<EventGroup>();

    /// <summary>應變人員清單</summary>
    public ICollection<EventResponder> Responders { get; set; } = new List<EventResponder>();

    /// <summary>Outbox 待派送訊息清單</summary>
    public ICollection<OutboxMessage> OutboxMessages { get; set; } = new List<OutboxMessage>();
}
