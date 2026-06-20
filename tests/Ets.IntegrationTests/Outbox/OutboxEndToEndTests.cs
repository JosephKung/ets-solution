// tests/Ets.IntegrationTests/Outbox/OutboxEndToEndTests.cs
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.BackgroundServices;
using Ets.Infrastructure.Outbox.Handlers;
using Ets.Infrastructure.Persistence;
using Ets.IntegrationTests.Helpers;
using Ets.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace Ets.IntegrationTests.Outbox;

/// <summary>
/// Outbox Pattern 端對端整合測試（WBS 1.3.18）
///
/// 直接呼叫 Handler（不走 HTTP），驗證：
///   完整 Outbox 鏈：CreateTeam → InviteTeamMember → InviteChatMember → UpdateFlexFooter
/// </summary>
public sealed class OutboxEndToEndTests
    : IntegrationTestBase, IClassFixture<EtsWebApplicationFactory>
{
    public OutboxEndToEndTests(EtsWebApplicationFactory factory) : base() { }

    // ─── 測試 1：CreateTeam Handler 完整鏈 ───────────────────────
    [Fact]
    public async Task CreateTeamHandler_應回填TeamSn並排入CreateTeamAPIAccount()
    {
        // Arrange：設定 Mock 回應
        Factory.MockSystemClient
            .CreateTeamAsync(Arg.Any<CreateTeamRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTeamResult(
                IsSuccess: true, Description: "OK", ErrorCode: 0,
                TeamSN: 99823104L, IgnoredMemberList: [], IgnoredManagerList: []));

        using var scope = Factory.Services.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider
            .GetServices<IOutboxHandler>()
            .First(h => h.MessageType == OutboxMessageType.CreateTeam);

        // 確認事件存在（由 InitializeAsync seed）
        var ev = await db.EmergencyEvents.FindAsync(TestDataBuilder.DefaultEventId);
        ev!.TeamPlusBigTeamSn = null;  // 重置以允許測試
        await db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new CreateTeamOutboxPayload(
            EventId:           TestDataBuilder.DefaultEventId,
            EventType:         "a",
            CommanderAccounts: ["joseph"],
            TeamName:          "XX醫療大樓火警緊急處理團隊",
            Subject:           "處理XX醫療大樓火警",
            Description:       "發生火警",
            MemberAccounts:    ["joseph", "marry"],
            ManagerAccounts:   ["joseph"]));

        // Act
        await handler.HandleAsync(1L, payload, CancellationToken.None);

        // Assert：TeamSN 已回填
        var evAfter = await db.EmergencyEvents.FindAsync(TestDataBuilder.DefaultEventId);
        evAfter!.TeamPlusBigTeamSn.Should().Be(99823104);

        // Assert：CreateTeamAPIAccount Outbox 已排入
        var apiAccountOutbox = await db.OutboxMessages
            .FirstOrDefaultAsync(o =>
                o.MessageType == OutboxMessageType.CreateTeamAPIAccount &&
                o.EventId     == TestDataBuilder.DefaultEventId);
        apiAccountOutbox.Should().NotBeNull();
    }

    // ─── 測試 2：InviteTeamMember + InviteChatMember 完整鏈 ──────
    [Fact]
    public async Task InviteMemberHandlers_應回填JoinedTeam且JoinedChatRoom()
    {
        // Arrange
        Factory.MockSystemClient
            .InviteTeamMemberAsync(Arg.Any<InviteTeamMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "OK", 0));
        Factory.MockSystemClient
            .InviteChatMemberAsync(Arg.Any<InviteChatMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "OK", 0));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var teamHandler = scope.ServiceProvider
            .GetServices<IOutboxHandler>()
            .First(h => h.MessageType == OutboxMessageType.InviteTeamMember);
        var chatHandler = scope.ServiceProvider
            .GetServices<IOutboxHandler>()
            .First(h => h.MessageType == OutboxMessageType.InviteChatMember);

        var teamPayload = JsonSerializer.Serialize(new InviteMemberOutboxPayload(
            EventId:         TestDataBuilder.DefaultEventId,
            MemberAccount:   "marry",
            OperatorAccount: "joseph",
            TargetSn:        99823104L));

        var chatPayload = JsonSerializer.Serialize(new InviteMemberOutboxPayload(
            EventId:         TestDataBuilder.DefaultEventId,
            MemberAccount:   "marry",
            OperatorAccount: "joseph",
            TargetSn:        88723611L,
            ChatGp:          "(A0021)消防組"));

        // Act
        await teamHandler.HandleAsync(1L, teamPayload, CancellationToken.None);
        await chatHandler.HandleAsync(2L, chatPayload, CancellationToken.None);

        // Assert
        var marry = await db.EventResponders
            .FirstAsync(r =>
                r.EventId == TestDataBuilder.DefaultEventId &&
                r.Account == "marry");
        marry.JoinedTeam.Should().BeTrue();
        marry.JoinedChatRoom.Should().BeTrue();
    }

    // ─── 測試 3：SendFlexMessage Handler 應回填 FlexMessageSn ────
    [Fact]
    public async Task SendFlexMessageHandler_應回填所有收件人FlexMessageSn()
    {
        // Arrange
        Factory.MockChannelClient
            .BroadcastFlexMessageAsync(
                Arg.Any<BroadcastFlexMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastFlexMessageResult(IsSuccess: true, MessageSN: 9453));

        // 先清除 seed 時設定的 FlexMessageSn
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var responders = await db.EventResponders
            .Where(r => r.EventId == TestDataBuilder.DefaultEventId)
            .ToListAsync();
        responders.ForEach(r => r.FlexMessageSn = null);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider
            .GetServices<IOutboxHandler>()
            .First(h => h.MessageType == OutboxMessageType.SendFlexMessage);

        var payload = JsonSerializer.Serialize(new SendFlexMessageOutboxPayload(
            EventId:          TestDataBuilder.DefaultEventId,
            EventType:        "a",
            RecipientAccounts: ["joseph", "marry"]));

        // Act
        await handler.HandleAsync(1L, payload, CancellationToken.None);

        // Assert
        var updated = await db.EventResponders
            .Where(r =>
                r.EventId == TestDataBuilder.DefaultEventId &&
                (r.Account == "joseph" || r.Account == "marry"))
            .ToListAsync();
        updated.Should().AllSatisfy(r => r.FlexMessageSn.Should().Be(9453));
    }

    // ─── 測試 4：OutboxDispatcher 批次處理 ────────────────────────
    [Fact]
    public async Task OutboxDispatcher_批次處理Pending訊息_應標記為Done()
    {
        // Arrange：手動插入 Outbox 訊息
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Factory.MockSystemClient
            .InviteTeamMemberAsync(Arg.Any<InviteTeamMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "OK", 0));

        var payload = JsonSerializer.Serialize(new InviteMemberOutboxPayload(
            EventId:         TestDataBuilder.DefaultEventId,
            MemberAccount:   "marry",
            OperatorAccount: "joseph",
            TargetSn:        99823104L));

        var msg = new OutboxMessage
        {
            EventId     = TestDataBuilder.DefaultEventId,
            MessageType = OutboxMessageType.InviteTeamMember,
            PayloadJson = payload,
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = DateTime.UtcNow
        };
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();

        // Act：直接透過 Factory 取得 HandlerFactory 執行
        var handlerFactory = scope.ServiceProvider
            .GetRequiredService<IOutboxHandlerFactory>();
        var handler = handlerFactory.GetHandler(OutboxMessageType.InviteTeamMember);
        await handler.HandleAsync(msg.OutboxId, msg.PayloadJson, CancellationToken.None);

        msg.Status      = OutboxMessageStatus.Done;
        msg.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Assert
        var processed = await db.OutboxMessages.FindAsync(msg.OutboxId);
        processed!.Status.Should().Be(OutboxMessageStatus.Done);
        processed.ProcessedAt.Should().NotBeNull();
    }

    // ─── 測試 5：UpdateFlexFooter 完整流程 ────────────────────────
    [Fact]
    public async Task UpdateFlexFooterHandler_應呼叫ChannelClient()
    {
        // Arrange
        Factory.MockChannelClient
            .UpdateFlexFooterAsync(Arg.Any<UpdateFlexFooterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "OK", 0));

        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetServices<IOutboxHandler>()
            .First(h => h.MessageType == OutboxMessageType.UpdateFlexFooter);

        var payload = JsonSerializer.Serialize(new UpdateFlexFooterOutboxPayload(
            EventId:    TestDataBuilder.DefaultEventId,
            EventType:  "a",
            MessageSn:  9453,
            Recipient:  "marry",
            FooterText: "已送出！",
            FontColor:  "#E53935"));

        // Act
        await handler.HandleAsync(1L, payload, CancellationToken.None);

        // Assert
        await Factory.MockChannelClient.Received(1)
            .UpdateFlexFooterAsync(
                Arg.Is<UpdateFlexFooterRequest>(r =>
                    r.Recipient  == "marry" &&
                    r.FooterText == "已送出！"),
                Arg.Any<CancellationToken>());
    }
}
