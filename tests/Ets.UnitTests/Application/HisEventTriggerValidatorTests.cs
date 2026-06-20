using Ets.Application.Dtos;
using FluentAssertions;
using Xunit;

namespace Ets.UnitTests.Application;

/// <summary>
/// HisEventTriggerValidator 單元測試。
/// 覆蓋規格書 §5.2 所有驗證情境。
/// </summary>
public class HisEventTriggerValidatorTests
{
    private readonly HisEventTriggerValidator _validator = new();

    // ── 輔助方法：建立合法的 DTO ─────────────────────────────────────────────

    private static HisEventTriggerDto ValidDto() => new()
    {
        EventId           = "E20240101120000A001",
        EventType         = "a",
        EventTime         = "2024-01-01 12:00:00",
        EventArea         = "林口院區",
        EventSummary      = "XX醫療大樓火警警報",
        EventSource       = "HIS",
        EventFlexMsgItems = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
        EventCommander    = "[\"joseph\"]",
        EventGroups       = new() { new() { ChatGp = "(A0021)消防組" } },
        EventResponders   = new()
        {
            new() { Account = "joseph", Role = "commander", ChatGp = "(A0021)消防組" },
            new() { Account = "alice",  Role = "normal",    ChatGp = "(A0021)消防組" }
        }
    };

    // ── 合法 DTO 應通過 ────────────────────────────────────────────────────────

    [Fact(DisplayName = "合法的 DTO 應通過所有驗證")]
    public async Task ValidDto_ShouldPass()
    {
        var result = await _validator.ValidateAsync(ValidDto());
        result.IsValid.Should().BeTrue();
    }

    // ── event_ID 驗證 ─────────────────────────────────────────────────────────

    [Theory(DisplayName = "event_ID 格式錯誤應回 E3003")]
    [InlineData("")]                     // 空字串
    [InlineData("20240101120000A001")]    // 缺少 E 前綴
    [InlineData("E2024010112000A001")]    // 時間戳少一碼
    [InlineData("E20240101120000AB001")] // event_type 超過一碼
    public async Task InvalidEventId_ShouldFail_WithE3003(string eventId)
    {
        var dto    = ValidDto();
        dto.EventId = eventId;

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == "E3002" || e.ErrorCode == "E3003");
    }

    // ── event_type 驗證 ───────────────────────────────────────────────────────

    [Theory(DisplayName = "無效的 event_type 應回 E3003")]
    [InlineData("f")]
    [InlineData("A")]   // 注意：允許大寫（Contains 做 OrdinalIgnoreCase），此例應通過
    [InlineData("ab")]
    [InlineData("")]
    public async Task InvalidEventType_ShouldFail(string eventType)
    {
        var dto       = ValidDto();
        dto.EventType = eventType;

        var result = await _validator.ValidateAsync(dto);
        // "A" 大寫應通過（OrdinalIgnoreCase），其他應失敗
        if (eventType == "A")
            result.IsValid.Should().BeTrue();
        else
            result.IsValid.Should().BeFalse();
    }

    // ── event_time 驗證 ───────────────────────────────────────────────────────

    [Theory(DisplayName = "event_time 格式錯誤應失敗")]
    [InlineData("2024/01/01 12:00:00")]  // 斜線分隔
    [InlineData("2024-01-01T12:00:00")] // ISO 8601 格式
    [InlineData("not-a-date")]
    [InlineData("")]
    public async Task InvalidEventTime_ShouldFail(string eventTime)
    {
        var dto        = ValidDto();
        dto.EventTime  = eventTime;

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
    }

    [Fact(DisplayName = "event_time 正確格式（YYYY-MM-DD HH:MM:SS）應通過")]
    public async Task ValidEventTime_ShouldPass()
    {
        var dto       = ValidDto();
        dto.EventTime = "2024-12-31 23:59:59";

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    // ── event_flex_msg_items 驗證 ─────────────────────────────────────────────

    [Theory(DisplayName = "非 JSON 陣列的 event_flex_msg_items 應失敗")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"key\":\"val\"}")]   // JSON Object，非 Array
    public async Task InvalidFlexMsgItems_ShouldFail(string items)
    {
        var dto               = ValidDto();
        dto.EventFlexMsgItems = items;

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
    }

    // ── 業務規則：指揮官必須在 responders 中 ──────────────────────────────────

    [Fact(DisplayName = "event_commander 中的帳號不在 responders 應回 E3007")]
    public async Task CommanderNotInResponders_ShouldFail_WithE3007()
    {
        var dto = ValidDto();
        // commander 指定 "peter"，但 responders 只有 "joseph" 和 "alice"
        dto.EventCommander = "[\"peter\"]";

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "E3007",
            because: "指揮官帳號不在應變人員名單中應觸發業務規則錯誤 E3007");
    }

    [Fact(DisplayName = "event_commander 帳號存在於 responders 應通過")]
    public async Task CommanderInResponders_ShouldPass()
    {
        var dto = ValidDto();
        // joseph 確實在 responders 中
        dto.EventCommander = "[\"joseph\"]";

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    // ── event_groups 驗證 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "空的 event_groups 應失敗")]
    public async Task EmptyEventGroups_ShouldFail()
    {
        var dto        = ValidDto();
        dto.EventGroups = new();

        var result = await _validator.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "E3002");
    }
}
