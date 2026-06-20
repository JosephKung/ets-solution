// tests/Ets.UnitTests/Application/SendFlexMessageOutboxHandlerTests.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Entities;
using Ets.Infrastructure.Outbox.Handlers;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Application;

public sealed class SendFlexMessageOutboxHandlerTests
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
            EventSummary      = "XX醫療大樓火警警報",
            EventDescription  = "台北市XX醫療大樓發生火警",
            EventTime         = new DateTime(2024, 1, 1, 12, 0, 0),
            FlexMsgItemsJson  = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        });
        db.EventResponders.AddRange(
            new EventResponder
            {
                EventId = "E20240101120000A001", Account = "joseph",
                Role = "mgr", ChatGp = "(A0021)消防組",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            },
            new EventResponder
            {
                EventId = "E20240101120000A001", Account = "marry",
                Role = "normal", ChatGp = "(A0021)消防組",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        return db;
    }

    private static string BuildPayloadJson() =>
        JsonSerializer.Serialize(new SendFlexMessageOutboxPayload(
            EventId:          "E20240101120000A001",
            EventType:        "a",
            RecipientAccounts: ["joseph", "marry"]));

    // ─── 測試 1：成功廣播，所有收件人 FlexMessageSn 應被回填 ─────
    [Fact]
    public async Task HandleAsync_成功廣播_應回填所有收件人FlexMessageSn()
    {
        var db = await SetupDbAsync(
            nameof(HandleAsync_成功廣播_應回填所有收件人FlexMessageSn));

        var mockClient  = Substitute.For<ITeamPlusChannelClient>();
        mockClient.BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastFlexMessageResult(IsSuccess: true, MessageSN: 9453));

        var handler = new SendFlexMessageOutboxHandler(
            mockClient,
            new FlexMessageBuilder(),
            db,
            NullLogger<SendFlexMessageOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        var responders = await db.EventResponders
            .Where(r => r.EventId == "E20240101120000A001")
            .ToListAsync();

        responders.Should().AllSatisfy(r => r.FlexMessageSn.Should().Be(9453));
    }

    // ─── 測試 2：冪等性 — 所有人已有 FlexMessageSn 時跳過 ────────
    [Fact]
    public async Task HandleAsync_所有人已有FlexMessageSn_應冪等跳過()
    {
        var db = await SetupDbAsync(
            nameof(HandleAsync_所有人已有FlexMessageSn_應冪等跳過));

        // 預先設定所有人已有 FlexMessageSn
        var responders = await db.EventResponders.ToListAsync();
        responders.ForEach(r => r.FlexMessageSn = 9999);
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        var handler    = new SendFlexMessageOutboxHandler(
            mockClient, new FlexMessageBuilder(), db,
            NullLogger<SendFlexMessageOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        await mockClient.DidNotReceive()
            .BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(),
                Arg.Any<CancellationToken>());
    }

    // ─── 測試 3：API 失敗應拋出例外 ──────────────────────────────
    [Fact]
    public async Task HandleAsync_API失敗_應拋出例外()
    {
        var db = await SetupDbAsync(nameof(HandleAsync_API失敗_應拋出例外));

        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        mockClient.BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastFlexMessageResult(IsSuccess: false, MessageSN: 0));

        var handler = new SendFlexMessageOutboxHandler(
            mockClient, new FlexMessageBuilder(), db,
            NullLogger<SendFlexMessageOutboxHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None));
    }
}

// ── FlexMessageBuilder 獨立測試 ──────────────────────────────────
public sealed class FlexMessageBuilderTests
{
    private static EmergencyEvent BuildEvent(
        string? description = "台北市XX醫療大樓發生火警",
        string flexItems = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]") =>
        new()
        {
            EventId          = "E20240101120000A001",
            EventType        = "a",
            EventSummary     = "XX醫療大樓火警警報",
            EventDescription = description,
            EventTime        = new DateTime(2024, 1, 1, 12, 0, 0),
            FlexMsgItemsJson = flexItems,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        };

    [Fact]
    public void BuildContentsWithButtons_應包含Footer與PostbackButtons()
    {
        var builder  = new FlexMessageBuilder();
        var ev       = BuildEvent();
        var contents = builder.BuildContentsWithButtons(ev);
        var json     = JsonSerializer.Serialize(contents);

        json.Should().Contain("postbackbutton");
        json.Should().Contain("15 分鐘內");
        json.Should().Contain("無法返回院區");
        json.Should().Contain("secondary");  // 無法返回院區 style
        json.Should().Contain("primary");    // 15/30 分鐘 style
        json.Should().Contain("footercontainer");
    }

    [Fact]
    public void BuildContentsWithoutButtons_不應包含Footer()
    {
        var builder  = new FlexMessageBuilder();
        var ev       = BuildEvent();
        var contents = builder.BuildContentsWithoutButtons(ev);
        var json     = JsonSerializer.Serialize(contents);

        json.Should().NotContain("footercontainer");
        json.Should().NotContain("postbackbutton");
        json.Should().Contain("僅通知");
    }

    [Fact]
    public void BuildContentsWithButtons_PostbackData應包含EventId與feedback()
    {
        var builder  = new FlexMessageBuilder();
        var ev       = BuildEvent();
        var contents = builder.BuildContentsWithButtons(ev);
        var json     = JsonSerializer.Serialize(contents);

        json.Should().Contain("id=E20240101120000A001");
        json.Should().Contain("feedback=");
    }

    [Fact]
    public void BuildContentsWithButtons_空FlexMsgItems_不應拋出例外()
    {
        var builder  = new FlexMessageBuilder();
        var ev       = BuildEvent(flexItems: "");
        var act      = () => builder.BuildContentsWithButtons(ev);
        act.Should().NotThrow();
    }
}
