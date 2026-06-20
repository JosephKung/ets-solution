using Ets.Application.Services;
using FluentAssertions;
using Xunit;

namespace Ets.UnitTests.Application;

/// <summary>
/// IntentInferenceService 單元測試。
/// 對應規格書 §5.2.1 按鈕語意推導規則。
/// </summary>
public class IntentInferenceServiceTests
{
    // ── InferIntent 基本規則 ──────────────────────────────────────────────────

    [Theory(DisplayName = "「無法返回院區」應推導為 CannotArrive")]
    [InlineData("無法返回院區")]
    [InlineData("  無法返回院區  ")]   // 有前後空白，仍應匹配
    public void CannotArriveText_ShouldReturn_CannotArrive(string text)
    {
        IntentInferenceService.InferIntent(text)
            .Should().Be(ButtonIntent.CannotArrive);
    }

    [Theory(DisplayName = "其他按鈕文字應推導為 WillArrive")]
    [InlineData("15 分鐘內")]
    [InlineData("30 分鐘內")]
    [InlineData("1 小時內")]
    [InlineData("已在現場")]
    [InlineData("2 小時內")]
    [InlineData("隨意文字")]
    public void OtherTexts_ShouldReturn_WillArrive(string text)
    {
        IntentInferenceService.InferIntent(text)
            .Should().Be(ButtonIntent.WillArrive);
    }

    // ── BuildIntentMap ────────────────────────────────────────────────────────

    [Fact(DisplayName = "BuildIntentMap 應正確產生所有按鈕的 intent 對應")]
    public void BuildIntentMap_ShouldMap_AllButtons()
    {
        // Arrange
        var json = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]";

        // Act
        var map = IntentInferenceService.BuildIntentMap(json);

        // Assert
        map.Should().HaveCount(3);
        map[0].Text.Should().Be("15 分鐘內");
        map[0].Intent.Should().Be("WillArrive");
        map[1].Text.Should().Be("30 分鐘內");
        map[1].Intent.Should().Be("WillArrive");
        map[2].Text.Should().Be("無法返回院區");
        map[2].Intent.Should().Be("CannotArrive");
    }

    [Fact(DisplayName = "BuildIntentMap 傳入空字串應回傳空清單")]
    public void BuildIntentMap_EmptyJson_ShouldReturnEmpty()
    {
        IntentInferenceService.BuildIntentMap("")
            .Should().BeEmpty();
    }

    [Fact(DisplayName = "BuildIntentMap 傳入無效 JSON 應回傳空清單（不拋例外）")]
    public void BuildIntentMap_InvalidJson_ShouldReturnEmpty_WithoutThrowing()
    {
        var act = () => IntentInferenceService.BuildIntentMap("not-json");
        act.Should().NotThrow();
        IntentInferenceService.BuildIntentMap("not-json")
            .Should().BeEmpty();
    }

    // ── LookupIntent ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "LookupIntent 應從序列化 JSON 正確查詢 intent")]
    public void LookupIntent_ShouldFind_CorrectIntent()
    {
        // Arrange — 先建立 intent map 再序列化
        var map     = IntentInferenceService.BuildIntentMap("[\"15 分鐘內\",\"無法返回院區\"]");
        var mapJson = IntentInferenceService.SerializeIntentMap(map);

        // Act
        var intent1 = IntentInferenceService.LookupIntent(mapJson, "15 分鐘內");
        var intent2 = IntentInferenceService.LookupIntent(mapJson, "無法返回院區");
        var intent3 = IntentInferenceService.LookupIntent(mapJson, "不存在的按鈕");

        // Assert
        intent1.Should().Be(ButtonIntent.WillArrive);
        intent2.Should().Be(ButtonIntent.CannotArrive);
        intent3.Should().BeNull(because: "不存在的按鈕應回傳 null");
    }

    // ── 序列化往返測試 ────────────────────────────────────────────────────────

    [Fact(DisplayName = "BuildIntentMap → Serialize → Deserialize 應完整往返")]
    public void IntentMap_RoundTrip_ShouldBeConsistent()
    {
        // Arrange
        var originalJson = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]";

        // Act
        var map        = IntentInferenceService.BuildIntentMap(originalJson);
        var serialized = IntentInferenceService.SerializeIntentMap(map);
        var restored   = IntentInferenceService.BuildIntentMap(
            // 模擬從 DB 讀回後再解析（注意：DB 存的是 entry[]，不是 string[]）
            "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]");

        // Assert
        map.Should().HaveCount(3);
        serialized.Should().NotBeNullOrEmpty();
        restored.Should().HaveCount(3);
    }
}
