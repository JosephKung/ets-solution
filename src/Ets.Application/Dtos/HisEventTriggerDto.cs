using FluentValidation;
using System.Text.Json.Serialization;

namespace Ets.Application.Dtos;

// ════════════════════════════════════════════════════════════════════════════
// HIS 事件觸發 Request DTO
// 對應規格書 §5.2 Request Body 欄位定義
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 分組交談室定義（HIS 傳入）
/// </summary>
public sealed class HisEventGroupDto
{
    /// <summary>分組交談室名稱（如 "(A0021)消防組"）— team+ 限制 20 字元，ETS 自動截斷</summary>
    [JsonPropertyName("chatGP")]
    public string ChatGp { get; set; } = string.Empty;

    /// <summary>分組描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// 應變人員定義（HIS 傳入）
/// </summary>
public sealed class HisEventResponderDto
{
    /// <summary>team+ 帳號</summary>
    [JsonPropertyName("acct")]
    public string Account { get; set; } = string.Empty;

    /// <summary>原始描述（含姓名與電話，如 "消防組-李小華(0982763543)"）</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>角色（commander/mgr/vice_mgr/normal/contacts/observer）</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>應加入之分組交談室名稱</summary>
    [JsonPropertyName("chatGP")]
    public string ChatGp { get; set; } = string.Empty;
}

/// <summary>
/// HIS 事件觸發 Request DTO。
/// 對應規格書 §5.2 完整欄位定義。
/// JSON Property Name 遵循 HIS 端約定（snake_case）。
/// </summary>
public sealed class HisEventTriggerDto
{
    /// <summary>唯一事件識別碼。格式：E + YYYYMMDDHHMMSS + event_type(1碼) + 3碼流水號</summary>
    [JsonPropertyName("event_ID")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>事件類型（a~e）</summary>
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>HIS 觸發時間（格式：YYYY-MM-DD HH:MM:SS）</summary>
    [JsonPropertyName("event_time")]
    public string EventTime { get; set; } = string.Empty;

    /// <summary>應變區域名稱（需通過 §5.3 白名單驗證）</summary>
    [JsonPropertyName("event_area")]
    public string? EventArea { get; set; }

    /// <summary>事件摘要（作為 Flex Message 卡片標題）</summary>
    [JsonPropertyName("event_summary")]
    public string EventSummary { get; set; } = string.Empty;

    /// <summary>事件詳細描述（選填）</summary>
    [JsonPropertyName("event_description")]
    public string? EventDescription { get; set; }

    /// <summary>觸發來源（預設 "HIS"）</summary>
    [JsonPropertyName("event_source")]
    public string EventSource { get; set; } = "HIS";

    /// <summary>語音播報文字（選填）</summary>
    [JsonPropertyName("audio_content")]
    public string? AudioContent { get; set; }

    /// <summary>
    /// Flex Message 按鈕項目 JSON 陣列字串。
    /// 範例：["15 分鐘內","30 分鐘內","無法返回院區"]
    /// </summary>
    [JsonPropertyName("event_flex_msg_items")]
    public string EventFlexMsgItems { get; set; } = string.Empty;

    /// <summary>分組交談室清單</summary>
    [JsonPropertyName("event_groups")]
    public List<HisEventGroupDto> EventGroups { get; set; } = new();

    /// <summary>指揮官帳號清單（JSON 陣列字串，如 "[\"joseph\",\"peter\"]"）</summary>
    [JsonPropertyName("event_commander")]
    public string EventCommander { get; set; } = string.Empty;

    /// <summary>應變人員清單</summary>
    [JsonPropertyName("event_responders")]
    public List<HisEventResponderDto> EventResponders { get; set; } = new();
}

// ════════════════════════════════════════════════════════════════════════════
// FluentValidation Validator
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// HIS 事件觸發 DTO 驗證器。
/// 對應規格書 §5.2 欄位定義與錯誤碼。
/// </summary>
public sealed class HisEventTriggerValidator : AbstractValidator<HisEventTriggerDto>
{
    /// <summary>允許的 event_type 值（a~e）</summary>
    private static readonly HashSet<string> ValidEventTypes =
        new(StringComparer.OrdinalIgnoreCase) { "a", "b", "c", "d", "e" };

    /// <summary>允許的 role 值</summary>
    private static readonly HashSet<string> ValidRoles =
        new(StringComparer.OrdinalIgnoreCase)
        { "commander", "mgr", "vice_mgr", "normal", "contacts", "observer" };

    /// <summary>event_ID 正規表達式：E + 14碼時間 + 1碼event_type + 3碼流水號</summary>
    private static readonly System.Text.RegularExpressions.Regex EventIdRegex =
        new(@"^E\d{14}[a-zA-Z]\d{3}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public HisEventTriggerValidator()
    {
        // ── event_ID ──────────────────────────────────────────────────────
        RuleFor(x => x.EventId)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_ID 為必填欄位")
            .Matches(EventIdRegex).WithErrorCode("E3003")
                .WithMessage("event_ID 格式錯誤，應為 E + YYYYMMDDHHMMSS + event_type + 3碼流水號（如 E20240101120000A001）");

        // ── event_type ────────────────────────────────────────────────────
        RuleFor(x => x.EventType)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_type 為必填欄位")
            .Must(t => ValidEventTypes.Contains(t))
                .WithErrorCode("E3003").WithMessage("event_type 必須為 a~e 之一");

        // ── event_time ────────────────────────────────────────────────────
        RuleFor(x => x.EventTime)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_time 為必填欄位")
            .Must(BeValidEventTime)
                .WithErrorCode("E3003").WithMessage("event_time 格式錯誤，應為 YYYY-MM-DD HH:MM:SS");

        // ── event_area ────────────────────────────────────────────────────
        // 注意：event_area 的白名單驗證在 Controller 層（需要注入 IAreaWhitelistService）
        RuleFor(x => x.EventArea)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_area 為必填欄位")
            .MaximumLength(50).WithErrorCode("E3003").WithMessage("event_area 不得超過 50 字元");

        // ── event_summary ─────────────────────────────────────────────────
        RuleFor(x => x.EventSummary)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_summary 為必填欄位")
            .MaximumLength(200).WithErrorCode("E3003").WithMessage("event_summary 不得超過 200 字元");

        // ── event_flex_msg_items ──────────────────────────────────────────
        RuleFor(x => x.EventFlexMsgItems)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_flex_msg_items 為必填欄位")
            .Must(BeValidJsonArray)
                .WithErrorCode("E3003").WithMessage("event_flex_msg_items 必須為合法 JSON 字串陣列");

        // ── event_groups ──────────────────────────────────────────────────
        RuleFor(x => x.EventGroups)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_groups 不得為空");

        RuleForEach(x => x.EventGroups)
            .ChildRules(group =>
            {
                group.RuleFor(g => g.ChatGp)
                    .NotEmpty().WithErrorCode("E3002").WithMessage("event_groups[].chatGP 為必填欄位")
                    .MaximumLength(100).WithErrorCode("E3003");
            });

        // ── event_commander ───────────────────────────────────────────────
        RuleFor(x => x.EventCommander)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_commander 為必填欄位")
            .Must(BeValidJsonArray)
                .WithErrorCode("E3003").WithMessage("event_commander 必須為合法 JSON 字串陣列");

        // ── event_responders ──────────────────────────────────────────────
        RuleFor(x => x.EventResponders)
            .NotEmpty().WithErrorCode("E3002").WithMessage("event_responders 不得為空");

        RuleForEach(x => x.EventResponders)
            .ChildRules(responder =>
            {
                responder.RuleFor(r => r.Account)
                    .NotEmpty().WithErrorCode("E3002").WithMessage("event_responders[].acct 為必填欄位");

                responder.RuleFor(r => r.Role)
                    .NotEmpty().WithErrorCode("E3002")
                    .Must(role => ValidRoles.Contains(role))
                        .WithErrorCode("E3003").WithMessage("role 必須為 commander/mgr/vice_mgr/normal/contacts/observer 之一");

                responder.RuleFor(r => r.ChatGp)
                    .NotEmpty().WithErrorCode("E3002").WithMessage("event_responders[].chatGP 為必填欄位");
            });

        // ── 業務規則：指揮官帳號必須出現在 responders 中 ─────────────────
        RuleFor(x => x)
            .Must(CommandersExistInResponders)
            .WithErrorCode("E3007")
            .WithMessage("event_commander 中的帳號必須存在於 event_responders 清單中");
    }

    // ── 自訂驗證方法 ──────────────────────────────────────────────────────────

    private static bool BeValidEventTime(string eventTime)
    {
        return DateTime.TryParseExact(
            eventTime,
            "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _);
    }

    private static bool BeValidJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    private static bool CommandersExistInResponders(HisEventTriggerDto dto)
    {
        if (!BeValidJsonArray(dto.EventCommander)) return true; // 格式問題交給其他規則
        try
        {
            var commanders = System.Text.Json.JsonSerializer
                .Deserialize<List<string>>(dto.EventCommander) ?? new();
            var responderAccounts = dto.EventResponders.Select(r => r.Account).ToHashSet();
            return commanders.All(c => responderAccounts.Contains(c));
        }
        catch
        {
            return true; // 解析失敗交給其他規則
        }
    }
}
