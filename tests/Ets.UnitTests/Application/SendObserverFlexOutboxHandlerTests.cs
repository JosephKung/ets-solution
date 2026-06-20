// tests/Ets.UnitTests/Application/SendObserverFlexOutboxHandlerTests.cs
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

public sealed class SendObserverFlexOutboxHandlerTests
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
            EventId          = "E20240101120000A001",
            EventType        = "a",
            EventSummary     = "火警",
            EventTime        = new DateTime(2024, 1, 1, 12, 0, 0),
            FlexMsgItemsJson = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        db.EventResponders.Add(new EventResponder
        {
            EventId   = "E20240101120000A001",
            Account   = "bob",
            Role      = "observer",
            ChatGp    = "(A0021)消防組",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static string BuildPayloadJson(IReadOnlyList<string>? observers = null) =>
        JsonSerializer.Serialize(new SendObserverFlexOutboxPayload(
            EventId:          "E20240101120000A001",
            EventType:        "a",
            ObserverAccounts: observers ?? ["bob"]));

    // ─── 測試 1：成功廣播，observer FlexMessageSn 應被回填 ───────
    [Fact]
    public async Task HandleAsync_成功廣播_應回填ObserverFlexMessageSn()
    {
        var db = await SetupDbAsync(
            nameof(HandleAsync_成功廣播_應回填ObserverFlexMessageSn));

        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        mockClient.BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastFlexMessageResult(IsSuccess: true, MessageSN: 8888));

        var handler = new SendObserverFlexOutboxHandler(
            mockClient, new FlexMessageBuilder(), db,
            NullLogger<SendObserverFlexOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        var observer = await db.EventResponders
            .FirstAsync(r => r.Account == "bob");
        observer.FlexMessageSn.Should().Be(8888);

        // 驗證使用無按鈕版本（body 不含 footer）
        var capturedRequest = (BroadcastFlexMessageRequest?)null;
        await mockClient.Received(1).BroadcastFlexMessageAsync(
            Arg.Do<BroadcastFlexMessageRequest>(r => capturedRequest = r),
            Arg.Any<CancellationToken>());

        capturedRequest.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(capturedRequest!.FlexContents);
        json.Should().NotContain("footercontainer");
    }

    // ─── 測試 2：ObserverAccounts 為空時直接跳過 ─────────────────
    [Fact]
    public async Task HandleAsync_空ObserverList_應直接跳過不呼叫API()
    {
        var db = await SetupDbAsync(
            nameof(HandleAsync_空ObserverList_應直接跳過不呼叫API));

        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        var handler    = new SendObserverFlexOutboxHandler(
            mockClient, new FlexMessageBuilder(), db,
            NullLogger<SendObserverFlexOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildPayloadJson([]), CancellationToken.None);

        await mockClient.DidNotReceive()
            .BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(),
                Arg.Any<CancellationToken>());
    }

    // ─── 測試 3：冪等性 — FlexMessageSn 已有值時跳過 ─────────────
    [Fact]
    public async Task HandleAsync_FlexMessageSn已有值_應冪等跳過()
    {
        var db = await SetupDbAsync(
            nameof(HandleAsync_FlexMessageSn已有值_應冪等跳過));

        var observer = await db.EventResponders.FirstAsync(r => r.Account == "bob");
        observer.FlexMessageSn = 9999;
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        var handler    = new SendObserverFlexOutboxHandler(
            mockClient, new FlexMessageBuilder(), db,
            NullLogger<SendObserverFlexOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        await mockClient.DidNotReceive()
            .BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(),
                Arg.Any<CancellationToken>());
    }
}
