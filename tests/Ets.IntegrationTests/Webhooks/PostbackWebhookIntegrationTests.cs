// tests/Ets.IntegrationTests/Webhooks/PostbackWebhookIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ets.Application.UseCases.Webhooks;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Ets.IntegrationTests.Helpers;
using Ets.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Ets.IntegrationTests.Webhooks;

/// <summary>
/// Postback Webhook 整合測試（WBS 1.3.17）
///
/// 測試完整流程：
///   HTTP POST → HMAC 驗證 → PostbackWebhookHandler → DB 更新 + Outbox 排入
///
/// 注意：Background Workers 在 Factory 中已停用，
///       Outbox 只需驗證「有排入」即可，不需等待 Worker 實際消費
/// </summary>
public sealed class PostbackWebhookIntegrationTests
    : IntegrationTestBase, IClassFixture<EtsWebApplicationFactory>
{
    private const string Endpoint   = "/api/v1/webhooks/teamplus/postback";
    // 對應 appsettings.Testing.json → TeamPlusChannels["a"].ChannelSecret
    private const string ChannelSecretA = "test-channel-secret-a";
    private const string ChannelIdA     = "test-channel-a";

    public PostbackWebhookIntegrationTests(EtsWebApplicationFactory factory)
        : base() { }

    // ─── 輔助方法 ──────────────────────────────────────────────────

    private static string BuildPostbackBody(
        string userId   = "marry",
        string feedback = "15 分鐘內",
        string eventId  = TestDataBuilder.DefaultEventId,
        long   timestamp = 1704096300000L)
    {
        var data = $"id={eventId}&feedback={Uri.EscapeDataString(feedback)}";
        var body = new
        {
            destination = ChannelIdA,
            events      = new[]
            {
                new
                {
                    type      = "postback",
                    timestamp,
                    source    = new { type = "user", userId },
                    postback  = new { data }
                }
            }
        };
        return JsonSerializer.Serialize(body);
    }

    private static string ComputeSignature(string channelSecret, string body)
    {
        var key   = Encoding.UTF8.GetBytes(channelSecret);
        var bytes = Encoding.UTF8.GetBytes(body);
        return Convert.ToBase64String(new HMACSHA256(key).ComputeHash(bytes));
    }

    private HttpRequestMessage MakeRequest(string bodyJson, string? signature = null)
    {
        signature ??= ComputeSignature(ChannelSecretA, bodyJson);
        return new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            Headers = { { "X-TeamPlus-Signature", signature } }
        };
    }

    // ─── 測試 1：WillArrive 完整流程 ───────────────────────────────
    [Fact]
    public async Task PostbackWillArrive_應更新ReplyStatus並排入InviteOutbox()
    {
        // Arrange
        var bodyJson = BuildPostbackBody("marry", "15 分鐘內");

        // Act
        var response = await Client.SendAsync(MakeRequest(bodyJson));

        // Assert：HTTP
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // Assert：DB
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var responder = await db.EventResponders
            .FirstAsync(r =>
                r.EventId == TestDataBuilder.DefaultEventId &&
                r.Account == "marry");
        responder.ReplyStatus.Should().Be("15 分鐘內");
        responder.ReplyChannel.Should().Be("Flex");

        var outbox = await db.OutboxMessages
            .Where(o => o.EventId == TestDataBuilder.DefaultEventId)
            .ToListAsync();
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.InviteTeamMember);
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.InviteChatMember);
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.UpdateFlexFooter);

        // Assert：SignalR 推播被觸發
        await Factory.MockNotifier.Received(1)
            .NotifyStatsChangedAsync(
                TestDataBuilder.DefaultEventId, Arg.Any<CancellationToken>());
    }

    // ─── 測試 2：CannotArrive 不排入 Invite Outbox ─────────────────
    [Fact]
    public async Task PostbackCannotArrive_不應排入InviteOutbox()
    {
        var bodyJson = BuildPostbackBody("marry", "無法返回院區");
        var response = await Client.SendAsync(MakeRequest(bodyJson));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var outbox = await db.OutboxMessages
            .Where(o => o.EventId == TestDataBuilder.DefaultEventId)
            .ToListAsync();
        outbox.Should().NotContain(o => o.MessageType == OutboxMessageType.InviteTeamMember);
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.UpdateFlexFooter);

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("無法返回院區");
    }

    // ─── 測試 3：HMAC 簽章錯誤仍回 200（記錄但不拒絕）────────────
    [Fact]
    public async Task PostbackInvalidSignature_仍回200但SignatureValid為False()
    {
        var bodyJson  = BuildPostbackBody("marry", "15 分鐘內", timestamp: 9999999999L);
        var badSig    = "invalidsignature==";
        var response  = await Client.SendAsync(MakeRequest(bodyJson, badSig));

        // team+ 規格：即使簽章錯誤也回 200（避免 team+ 重送）
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 但 WebhookInbox 應記錄 SignatureValid=false
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbox = await db.WebhookInboxes
            .OrderByDescending(w => w.ReceivedAt)
            .FirstOrDefaultAsync();
        inbox.Should().NotBeNull();
        inbox!.SignatureValid.Should().BeFalse();
    }

    // ─── 測試 4：冪等性 — 相同 body 第二次應跳過 ─────────────────
    [Fact]
    public async Task PostbackDuplicate_第二次應冪等跳過()
    {
        var bodyJson = BuildPostbackBody("marry", "30 分鐘內", timestamp: 1704096400000L);

        // 第一次
        await Client.SendAsync(MakeRequest(bodyJson));
        // 第二次（完全相同）
        await Client.SendAsync(MakeRequest(bodyJson));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // WebhookInbox 應只有 1 筆
        var inboxCount = await db.WebhookInboxes.CountAsync();
        inboxCount.Should().Be(1);

        // ReplyStatus 只被更新一次
        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("30 分鐘內");

        // SignalR 只推播一次
        await Factory.MockNotifier.Received(1)
            .NotifyStatsChangedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ─── 測試 5：Observer 回覆應被拒絕（記錄 AuditLog）───────────
    [Fact]
    public async Task PostbackObserver_應記錄AuditLog並忽略()
    {
        // "bob" 是 observer（TestDataBuilder.SeedStandardEventAsync 已建立）
        var bodyJson = BuildPostbackBody("bob", "15 分鐘內", timestamp: 1704096500000L);
        var response = await Client.SendAsync(MakeRequest(bodyJson));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var bob = await db.EventResponders.FirstAsync(r => r.Account == "bob");
        bob.ReplyStatus.Should().Be("Pending");

        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Detail!.Contains("W5004"));
        audit.Should().NotBeNull();
    }

    // ─── 測試 6：非法 feedback 應記錄 AuditLog ──────────────────
    [Fact]
    public async Task PostbackInvalidFeedback_應記錄AuditLog()
    {
        var bodyJson = BuildPostbackBody("marry", "非法按鈕", timestamp: 1704096600000L);
        await Client.SendAsync(MakeRequest(bodyJson));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var marry = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        marry.ReplyStatus.Should().Be("Pending");

        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Detail!.Contains("W5003"));
        audit.Should().NotBeNull();
    }

    // ─── 測試 7：非 postback 類型事件應略過 ──────────────────────
    [Fact]
    public async Task NonPostbackEvent_應略過並回200()
    {
        var body = new
        {
            destination = ChannelIdA,
            events      = new[]
            {
                new
                {
                    type      = "message",
                    timestamp = 1704096700000L,
                    source    = new { type = "user", userId = "marry" },
                    message   = new { type = "text", text = "hello" }
                }
            }
        };
        var bodyJson = JsonSerializer.Serialize(body);
        var response = await Client.SendAsync(MakeRequest(bodyJson));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var marry = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        marry.ReplyStatus.Should().Be("Pending");  // 未被更新
    }
}
