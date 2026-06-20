// tests/Ets.UnitTests/Application/PostbackWebhookHandlerTests.cs
using System.Text.Json;
using Ets.Application.Interfaces;
using Ets.Application.UseCases.Webhooks;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Ets.Infrastructure.Webhooks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Application;

public sealed class PostbackWebhookHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<AppDbContext> SetupDbAsync(string dbName)
    {
        var db = CreateDb(dbName);
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId           = "E20240101120000A001",
            EventType         = "a",
            EventSummary      = "火警",
            FlexMsgItemsJson  = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
            TeamPlusBigTeamSn = 99823104,
            Status            = EventStatus.Processing,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        });
        db.EventGroups.Add(new EventGroup
        {
            GroupId        = 1,
            EventId        = "E20240101120000A001",
            ChatGp         = "(A0021)消防組",
            TeamPlusChatSn = 88723611,
            CreatedAt      = DateTime.UtcNow
        });
        db.EventResponders.AddRange(
            new EventResponder
            {
                EventId       = "E20240101120000A001",
                Account       = "joseph",
                Role          = "commander",
                ChatGp        = "(A0021)消防組",
                ReplyStatus   = "Pending",
                FlexMessageSn = 9453,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },
            new EventResponder
            {
                EventId       = "E20240101120000A001",
                Account       = "marry",
                Role          = "normal",
                ChatGp        = "(A0021)消防組",
                ReplyStatus   = "Pending",
                FlexMessageSn = 9453,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        return db;
    }

    private static PostbackWebhookBody BuildBody(
        string userId   = "marry",
        string feedback = "15 分鐘內",
        string eventId  = "E20240101120000A001")
    {
        var data = $"id={eventId}&feedback={Uri.EscapeDataString(feedback)}";
        return new PostbackWebhookBody
        {
            Destination = "180284",
            Events =
            [
                new WebhookEvent
                {
                    Type      = "postback",
                    Timestamp = 1704096300000L,
                    Source    = new WebhookEventSource { Type = "user", UserId = userId },
                    Postback  = new WebhookPostback { Data = data }
                }
            ]
        };
    }

    [Fact]
    public async Task HandleAsync_WillArrive_應更新ReplyStatus並排入InviteOutbox()
    {
        var db       = await SetupDbAsync(
            nameof(HandleAsync_WillArrive_應更新ReplyStatus並排入InviteOutbox));
        var notifier = Substitute.For<IDashboardNotifier>();
        var handler  = new PostbackWebhookHandler(
            db, notifier, NullLogger<PostbackWebhookHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("marry", "15 分鐘內"), "{}", true, CancellationToken.None);

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("15 分鐘內");
        responder.ReplyChannel.Should().Be("Flex");

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.InviteTeamMember);
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.InviteChatMember);
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.UpdateFlexFooter);

        await notifier.Received(1)
            .NotifyStatsChangedAsync("E20240101120000A001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CannotArrive_不應排入InviteOutbox()
    {
        var db       = await SetupDbAsync(
            nameof(HandleAsync_CannotArrive_不應排入InviteOutbox));
        var notifier = Substitute.For<IDashboardNotifier>();
        var handler  = new PostbackWebhookHandler(
            db, notifier, NullLogger<PostbackWebhookHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("marry", "無法返回院區"), "{}", true, CancellationToken.None);

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("無法返回院區");

        var outbox = await db.OutboxMessages.ToListAsync();
        outbox.Should().NotContain(o => o.MessageType == OutboxMessageType.InviteTeamMember);
        outbox.Should().Contain(o => o.MessageType == OutboxMessageType.UpdateFlexFooter);
    }

    [Fact]
    public async Task HandleAsync_重複回覆_應冪等跳過()
    {
        var db       = await SetupDbAsync(nameof(HandleAsync_重複回覆_應冪等跳過));
        var notifier = Substitute.For<IDashboardNotifier>();
        var handler  = new PostbackWebhookHandler(
            db, notifier, NullLogger<PostbackWebhookHandler>.Instance);

        var body = BuildBody("marry", "15 分鐘內");
        await handler.HandleAsync(body, "{}", true, CancellationToken.None);
        await handler.HandleAsync(body, "{}", true, CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("15 分鐘內");
        await notifier.Received(1).NotifyStatsChangedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_非法feedback_應寫AuditLog並跳過()
    {
        var db       = await SetupDbAsync(
            nameof(HandleAsync_非法feedback_應寫AuditLog並跳過));
        var notifier = Substitute.For<IDashboardNotifier>();
        var handler  = new PostbackWebhookHandler(
            db, notifier, NullLogger<PostbackWebhookHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("marry", "非法按鈕文字"), "{}", true, CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "marry");
        responder.ReplyStatus.Should().Be("Pending");

        var auditLog = await db.AuditLogs.FirstOrDefaultAsync();
        auditLog.Should().NotBeNull();
        auditLog!.Detail.Should().Contain("W5003");
    }

    [Fact]
    public async Task HandleAsync_Observer回覆_應寫AuditLog並跳過()
    {
        var db = await SetupDbAsync(nameof(HandleAsync_Observer回覆_應寫AuditLog並跳過));
        db.EventResponders.Add(new EventResponder
        {
            EventId     = "E20240101120000A001",
            Account     = "bob",
            Role        = "observer",
            ChatGp      = "(A0021)消防組",
            ReplyStatus = "Pending",
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var notifier = Substitute.For<IDashboardNotifier>();
        var handler  = new PostbackWebhookHandler(
            db, notifier, NullLogger<PostbackWebhookHandler>.Instance);

        await handler.HandleAsync(
            BuildBody("bob", "15 分鐘內"), "{}", true, CancellationToken.None);

        var responder = await db.EventResponders.FirstAsync(r => r.Account == "bob");
        responder.ReplyStatus.Should().Be("Pending");
        var auditLog = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Detail!.Contains("W5004"));
        auditLog.Should().NotBeNull();
    }
}
