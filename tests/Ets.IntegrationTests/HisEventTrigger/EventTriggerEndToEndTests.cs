using Ets.Application.Abstractions;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Ets.IntegrationTests.Infrastructure;

namespace Ets.IntegrationTests.HisEventTrigger;

/// <summary>
/// HIS event-trigger API 端對端整合測試（1.2.10）。
/// 對應規格書 §16.2 關鍵測試案例 1~3。
///
/// 使用 EtsWebApplicationFactory（InMemory DB + Mock 外部依賴），
/// 測試完整 HTTP 請求 → Middleware → Controller → Handler → DB 寫入 流程。
/// </summary>
public class EventTriggerEndToEndTests : IClassFixture<EtsWebApplicationFactory>
{
    private readonly EtsWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const string Endpoint = "/api/v1/his/event-trigger";

    // 測試用 ChannelSecret（對應 appsettings.Test.json 的 channel 'a'）
    private const string ValidApiKey = "test-channel-secret-a";

    public EventTriggerEndToEndTests(EtsWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────────

    private static object ValidBody(
        string eventId   = "E20240101120000A001",
        string eventType = "a",
        string eventArea = "林口院區") => new
    {
        event_ID           = eventId,
        event_type         = eventType,
        event_time         = "2024-01-01 12:00:00",
        event_area         = eventArea,
        event_summary      = "XX醫療大樓火警警報",
        event_source       = "HIS",
        event_flex_msg_items = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
        event_commander    = "[\"joseph\"]",
        event_groups       = new[]
        {
            new { chatGP = "(A0020)火警應變小組", description = "火警應變小組" }
        },
        event_responders   = new[]
        {
            new { acct = "joseph", role = "commander", chatGP = "(A0020)火警應變小組" },
            new { acct = "alice",  role = "normal",    chatGP = "(A0020)火警應變小組" }
        }
    };

    private HttpRequestMessage MakeRequest(
        object body,
        string apiKey  = ValidApiKey,
        string? fromIp = "10.0.1.5") =>
        new(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body),
            Headers =
            {
                { "X-ETS-API-Key",     apiKey },
                { "X-Forwarded-For",   fromIp ?? "10.0.1.5" }
            }
        };

    // ── 情境 1：正常觸發（§16.2 case 1）──────────────────────────────────────

    [Fact(DisplayName = "合法請求應回 200 OK，DB 應寫入 EmergencyEvent + Groups + Responders + Outbox")]
    public async Task ValidRequest_ShouldReturn_200_AndPersistData()
    {
        // Arrange
        _factory.IpWhitelistService.IsAllowed(Arg.Any<string?>()).Returns(true);
        _factory.AreaWhitelistService.IsAllowed(Arg.Any<string?>()).Returns(true);

        var request = MakeRequest(ValidBody("E20240101120000A101"));

        // Act
        var response = await _client.SendAsync(request);

        // Assert — HTTP
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("data")
            .GetProperty("event_id").GetString()
            .Should().Be("E20240101120000A101");

        // Assert — DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ev = await db.EmergencyEvents.FindAsync("E20240101120000A101");
        ev.Should().NotBeNull();
        ev!.EventType.Should().Be("a");
        ev.EventArea.Should().Be("林口院區");
        ev.FlexMsgIntentMapJson.Should().Contain("WillArrive");
        ev.FlexMsgIntentMapJson.Should().Contain("CannotArrive");

        var groups = await db.EventGroups
            .Where(g => g.EventId == "E20240101120000A101")
            .ToListAsync();
        groups.Should().HaveCount(1);

        var responders = await db.EventResponders
            .Where(r => r.EventId == "E20240101120000A101")
            .ToListAsync();
        responders.Should().HaveCount(2);
        responders.Should().Contain(r => r.Account == "joseph" && r.Role == "commander");

        var outbox = await db.OutboxMessages
            .Where(o => o.EventId == "E20240101120000A101")
            .ToListAsync();
        // CreateTeam(1) + CreateChat(1) + SendFlex(1) = 3
        outbox.Should().HaveCount(3);
    }

    // ── 情境 2：事件 ID 防重（§16.2 case 1）─────────────────────────────────

    [Fact(DisplayName = "相同 EventId 重送應回 409 E3006，DB 中僅保留一筆")]
    public async Task DuplicateEventId_ShouldReturn_409_E3006()
    {
        // Arrange — 先送一次成功
        const string eventId = "E20240101120000A201";
        await _client.SendAsync(MakeRequest(ValidBody(eventId)));

        // Act — 重送相同 EventId
        var response = await _client.SendAsync(MakeRequest(ValidBody(eventId)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error_code").GetString().Should().Be("E3006");

        // DB 中應只有 1 筆
        using var scope = _factory.Services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count  = await db.EmergencyEvents.CountAsync(e => e.EventId == eventId);
        count.Should().Be(1, because: "防重後 DB 中應只有一筆事件");
    }

    // ── 情境 3：IP 白名單（§16.2 case 3）────────────────────────────────────

    [Fact(DisplayName = "IP 不在白名單應回 403 E3009")]
    public async Task BlockedIp_ShouldReturn_403_E3009()
    {
        // Arrange — 此請求的 IP 被拒絕
        _factory.IpWhitelistService.IsAllowed("8.8.8.8").Returns(false);

        var request = MakeRequest(ValidBody("E20240101120000A301"), fromIp: "8.8.8.8");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("error_code").GetString().Should().Be("E3009");
    }

    // ── 情境 4：API Key × event_type 不符（§16.2 case 2）────────────────────

    [Fact(DisplayName = "API Key 與 event_type 不符應回 401 E3010")]
    public async Task ApiKeyMismatch_ShouldReturn_401_E3010()
    {
        // Arrange — 使用 'a' 的 Key，但 Body 中 event_type='b'
        // 'b' 的 ChannelSecret 在 appsettings.Test.json 是 "test-channel-secret-b"
        var request = MakeRequest(
            ValidBody("E20240101120000A401", eventType: "b"),
            apiKey: "test-channel-secret-a");   // 錯誤：用 'a' 的 Key

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("error_code").GetString().Should().Be("E3010");
    }

    // ── 情境 5：缺少 API Key Header（E3001）──────────────────────────────────

    [Fact(DisplayName = "缺少 X-ETS-API-Key Header 應回 401 E3001")]
    public async Task MissingApiKeyHeader_ShouldReturn_401_E3001()
    {
        // Arrange — 不帶 X-ETS-API-Key
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(ValidBody("E20240101120000A501")),
            Headers = { { "X-Forwarded-For", "10.0.1.5" } }
        };

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("error_code").GetString().Should().Be("E3001");
    }

    // ── 情境 6：event_area 不在白名單（E3011）───────────────────────────────

    [Fact(DisplayName = "event_area 不在白名單應回 400 E3011")]
    public async Task AreaNotInWhitelist_ShouldReturn_400_E3011()
    {
        // Arrange — 白名單設為限制模式，只允許「林口院區」
        _factory.AreaWhitelistService.IsAllowed("林口院區").Returns(true);
        _factory.AreaWhitelistService.IsAllowed("未知院區").Returns(false);
        _factory.AreaWhitelistService.IsUnrestricted.Returns(false);

        var request = MakeRequest(
            ValidBody("E20240101120000A601", eventArea: "未知院區"));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("error_code").GetString().Should().Be("E3011");
    }

    // ── 情境 7：FluentValidation 必填欄位（E3002）────────────────────────────

    [Fact(DisplayName = "缺少 event_ID 應回 400 E3002")]
    public async Task MissingEventId_ShouldReturn_400_E3002()
    {
        var body = new
        {
            event_ID           = "",    // 空字串
            event_type         = "a",
            event_time         = "2024-01-01 12:00:00",
            event_summary      = "測試",
            event_flex_msg_items = "[\"15 分鐘內\"]",
            event_commander    = "[\"joseph\"]",
            event_groups       = new[] { new { chatGP = "(A001)消防組" } },
            event_responders   = new[] { new { acct = "joseph", role = "commander", chatGP = "(A001)消防組" } }
        };

        var request = MakeRequest(body);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var errorCode = json!.RootElement.GetProperty("error_code").GetString();
        errorCode.Should().BeOneOf("E3002", "E3003");
    }

    // ── 情境 8：Health Check 仍可存取（不被 Middleware 攔截）─────────────────

    [Fact(DisplayName = "/health/live 應回 200，不被 HisApiKeyMiddleware 攔截")]
    public async Task HealthLive_ShouldReturn_200_WithoutApiKey()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
