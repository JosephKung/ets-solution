// tests/Ets.IntegrationTests/Voice/VoiceCallbackIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Ets.Infrastructure.Persistence;
using Ets.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ets.IntegrationTests.Voice;

/// <summary>
/// M4 語音 Fallback 整合測試（WBS 1.4.9）
/// 模擬 8 階段 Voice Callback Webhook 完整流程
/// </summary>
public sealed class VoiceCallbackIntegrationTests
    : IntegrationTestBase, IClassFixture<EtsWebApplicationFactory>
{
    private const string Endpoint      = "/api/v1/webhooks/voicebot";
    private const string ValidApiKey   = "test-channel-secret-a";
    private const string ExternalCallId = "E20240101120000A001-9-1";

    public VoiceCallbackIntegrationTests(EtsWebApplicationFactory factory) : base() { }

    // ─── 輔助方法 ──────────────────────────────────────────────────

    private async Task SeedResponderWithCallIdAsync(
        string callId = ExternalCallId,
        string replyStatus = "Pending")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 更新 seed 資料中的 marry，設定 LastExternalCallId
        var marry = await db.EventResponders
            .FirstOrDefaultAsync(r => r.Account == "marry");
        if (marry is not null)
        {
            marry.LastExternalCallId = callId;
            marry.ReplyStatus        = replyStatus;
            await db.SaveChangesAsync();
        }
    }

    private HttpRequestMessage MakeRequest(string status, string callId = ExternalCallId)
    {
        var body = JsonSerializer.Serialize(new
        {
            external_call_id = callId,
            status           = status
        });
        return new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { { "X-ETS-API-Key", ValidApiKey } }
        };
    }

    // ─── 測試 1：完整 8 階段順序 ──────────────────────────────────
    [Fact]
    public async Task VoiceCallback_完整8階段_應正確更新LastVoiceStatus()
    {
        await SeedResponderWithCallIdAsync();

        var stages = new[]
        {
            "QUEUED", "DIALING", "RINGING", "ANSWERED",
            "PLAYING", "PLAY_DONE", "COMPLETED"
        };

        foreach (var stage in stages)
        {
            var response = await Client.SendAsync(MakeRequest(stage));
            response.StatusCode.Should().Be(
                HttpStatusCode.OK, $"stage={stage} 應回 200");
        }

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.LastVoiceStatus.Should().Be("COMPLETED");
        responder.ReplyStatus.Should().Be("VoiceConfirmed");
        responder.ReplyChannel.Should().Be("Voice");
    }

    // ─── 測試 2：REJECTED 終態，不再有後續 ────────────────────────
    [Fact]
    public async Task VoiceCallback_REJECTED_終態後LastVoiceStatus應為REJECTED()
    {
        await SeedResponderWithCallIdAsync();

        // RINGING → REJECTED
        await Client.SendAsync(MakeRequest("RINGING"));
        var response = await Client.SendAsync(MakeRequest("REJECTED"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.LastVoiceStatus.Should().Be("REJECTED");
        responder.ReplyStatus.Should().Be("Pending");  // REJECTED 不改 ReplyStatus
    }

    // ─── 測試 3：冪等性 — 相同 (CallId, Status) 第二次應跳過 ─────
    [Fact]
    public async Task VoiceCallback_相同Status重送_應冪等跳過()
    {
        await SeedResponderWithCallIdAsync();

        await Client.SendAsync(MakeRequest("RINGING"));
        await Client.SendAsync(MakeRequest("RINGING"));  // 重送

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inboxCount = await db.WebhookInboxes
            .CountAsync(w => w.ExternalMessageId!.Contains("RINGING"));
        inboxCount.Should().Be(1, "相同 (CallId,Status) 只應寫入一筆 WebhookInbox");
    }

    // ─── 測試 4：缺少 X-ETS-API-Key 應回 401 ─────────────────────
    [Fact]
    public async Task VoiceCallback_缺少ApiKey_應回401()
    {
        var body = JsonSerializer.Serialize(new
        {
            external_call_id = ExternalCallId,
            status           = "QUEUED"
        });
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
            // 故意不帶 X-ETS-API-Key
        };

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("error_code").GetString().Should().Be("E3001");
    }

    // ─── 測試 5：API Key 與 event_type 不符應回 401 E3010 ─────────
    [Fact]
    public async Task VoiceCallback_ApiKey與EventType不符_應回401E3010()
    {
        // ExternalCallId 第 16 碼 = 'A'（event_type a）
        // 使用 b 的 Key → 不符
        var body = JsonSerializer.Serialize(new
        {
            external_call_id = ExternalCallId,  // event_type = a
            status           = "QUEUED"
        });
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { { "X-ETS-API-Key", "test-channel-secret-b" } }  // b 的 Key
        };

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("error_code").GetString().Should().Be("E3010");
    }

    // ─── 測試 6：COMPLETED 且已 Flex 回覆 → ReplyStatus 不被覆蓋 ─
    [Fact]
    public async Task VoiceCallback_COMPLETED_但已Flex回覆_不覆蓋ReplyStatus()
    {
        // 預先設 marry 已回覆
        await SeedResponderWithCallIdAsync(replyStatus: "15 分鐘內");

        await Client.SendAsync(MakeRequest("COMPLETED"));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("15 分鐘內");  // 不被 Voice 覆蓋
        responder.LastVoiceStatus.Should().Be("COMPLETED");
    }

    // ─── 測試 7：COMPLETED → UpdateFlexFooter Outbox 應排入 ──────
    [Fact]
    public async Task VoiceCallback_COMPLETED_應排入UpdateFlexFooterOutbox()
    {
        await SeedResponderWithCallIdAsync();

        await Client.SendAsync(MakeRequest("COMPLETED"));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var outbox = await db.OutboxMessages
            .FirstOrDefaultAsync(o =>
                o.MessageType == OutboxMessageType.UpdateFlexFooter &&
                o.EventId     == "E20240101120000A001");
        outbox.Should().NotBeNull();
        outbox!.PayloadJson.Should().Contain("語音已送達");
    }

    // ─── 測試 8：找不到對應 Responder 仍回 200 ────────────────────
    [Fact]
    public async Task VoiceCallback_找不到Responder_仍回200()
    {
        // 使用不存在的 CallId
        var response = await Client.SendAsync(
            MakeRequest("QUEUED", "E99999999999999A001-9-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
