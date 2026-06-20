// tests/Ets.UnitTests/Application/VoiceCallbackHandlerTests.cs
using Ets.Application.UseCases.Voice;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Ets.Infrastructure.Webhooks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ets.UnitTests.Application;

public sealed class VoiceCallbackHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<AppDbContext> SetupDbAsync(
        string dbName,
        string replyStatus          = "Pending",
        string lastExternalCallId   = "E20240101120000A001-9-1",
        int? flexMessageSn          = 9453)
    {
        var db = CreateDb(dbName);
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventSummary = "火警",
            Status       = EventStatus.Processing,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        db.EventResponders.Add(new EventResponder
        {
            EventId             = "E20240101120000A001",
            Account             = "marry",
            Role                = "normal",
            ChatGp              = "(A0021)消防組",
            ReplyStatus         = replyStatus,
            LastExternalCallId  = lastExternalCallId,
            FlexMessageSn       = flexMessageSn,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static VoiceCallbackBody BuildBody(string callId, string status) =>
        new() { ExternalCallId = callId, Status = status };

    // ─── 測試 1：RINGING → 更新 LastVoiceStatus ──────────────────
    [Fact]
    public async Task HandleAsync_RINGING_應更新LastVoiceStatus()
    {
        var db      = await SetupDbAsync(nameof(HandleAsync_RINGING_應更新LastVoiceStatus));
        var handler = new VoiceCallbackHandler(db,
            NullLogger<VoiceCallbackHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("E20240101120000A001-9-1", "RINGING"),
            CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        responder.LastVoiceStatus.Should().Be("RINGING");
        responder.ReplyStatus.Should().Be("Pending");  // ReplyStatus 不變
    }

    // ─── 測試 2：COMPLETED → ReplyStatus 應轉為 VoiceConfirmed ───
    [Fact]
    public async Task HandleAsync_COMPLETED_且Pending_應轉VoiceConfirmed()
    {
        var db      = await SetupDbAsync(
            nameof(HandleAsync_COMPLETED_且Pending_應轉VoiceConfirmed));
        var handler = new VoiceCallbackHandler(db,
            NullLogger<VoiceCallbackHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("E20240101120000A001-9-1", "COMPLETED"),
            CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        responder.LastVoiceStatus.Should().Be("COMPLETED");
        responder.ReplyStatus.Should().Be("VoiceConfirmed");
        responder.ReplyChannel.Should().Be("Voice");

        // UpdateFlexFooter Outbox 應已排入
        var outbox = await db.OutboxMessages
            .FirstOrDefaultAsync(o => o.MessageType == OutboxMessageType.UpdateFlexFooter);
        outbox.Should().NotBeNull();
        outbox!.PayloadJson.Should().Contain("語音已送達");
    }

    // ─── 測試 3：COMPLETED 但已有回覆 → ReplyStatus 不應被覆蓋 ──
    [Fact]
    public async Task HandleAsync_COMPLETED_但已回覆_不應覆蓋ReplyStatus()
    {
        var db      = await SetupDbAsync(
            nameof(HandleAsync_COMPLETED_但已回覆_不應覆蓋ReplyStatus),
            replyStatus: "15 分鐘內");
        var handler = new VoiceCallbackHandler(db,
            NullLogger<VoiceCallbackHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("E20240101120000A001-9-1", "COMPLETED"),
            CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("15 分鐘內");  // 不被覆蓋
        responder.LastVoiceStatus.Should().Be("COMPLETED");
    }

    // ─── 測試 4：REJECTED → LastVoiceStatus 更新，ReplyStatus 不變 ──
    [Fact]
    public async Task HandleAsync_REJECTED_應更新LastVoiceStatus但不改ReplyStatus()
    {
        var db      = await SetupDbAsync(
            nameof(HandleAsync_REJECTED_應更新LastVoiceStatus但不改ReplyStatus));
        var handler = new VoiceCallbackHandler(db,
            NullLogger<VoiceCallbackHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("E20240101120000A001-9-1", "REJECTED"),
            CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        responder.LastVoiceStatus.Should().Be("REJECTED");
        responder.ReplyStatus.Should().Be("Pending");
    }

    // ─── 測試 5：冪等性 — 相同 (CallId, Status) 第二次應跳過 ────
    [Fact]
    public async Task HandleAsync_重複Status_應冪等跳過()
    {
        var db      = await SetupDbAsync(nameof(HandleAsync_重複Status_應冪等跳過));
        var handler = new VoiceCallbackHandler(db,
            NullLogger<VoiceCallbackHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("E20240101120000A001-9-1", "RINGING"),
            CancellationToken.None);
        await handler.HandleAsync(
            BuildBody("E20240101120000A001-9-1", "RINGING"),  // 相同
            CancellationToken.None);

        // WebhookInbox 只有 1 筆
        var inboxCount = await db.WebhookInboxes.CountAsync();
        inboxCount.Should().Be(1);
    }

    // ─── 測試 6：找不到對應 Responder 應寫 WebhookInbox 並繼續 ──
    [Fact]
    public async Task HandleAsync_找不到Responder_應寫Inbox並回200()
    {
        var db      = await SetupDbAsync(nameof(HandleAsync_找不到Responder_應寫Inbox並回200));
        var handler = new VoiceCallbackHandler(db,
            NullLogger<VoiceCallbackHandler>.Instance);

        // 使用不存在的 CallId
        var act = async () => await handler.HandleAsync(
            BuildBody("UNKNOWN-CALL-ID-9-1", "QUEUED"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();

        var inbox = await db.WebhookInboxes.FirstOrDefaultAsync();
        inbox.Should().NotBeNull();
    }
}
