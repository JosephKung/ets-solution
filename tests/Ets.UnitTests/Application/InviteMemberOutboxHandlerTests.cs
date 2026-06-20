// tests/Ets.UnitTests/Application/InviteMemberOutboxHandlerTests.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Outbox.Handlers;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Application;

public sealed class InviteMemberOutboxHandlerTests
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
        bool joinedTeam = false,
        bool joinedChatRoom = false)
    {
        var db = CreateDb(dbName);
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId           = "E20240101120000A001",
            EventType         = "a",
            EventSummary      = "火警",
            TeamPlusBigTeamSn = 99823104,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        });
        db.EventResponders.Add(new EventResponder
        {
            EventId      = "E20240101120000A001",
            Account      = "marry",
            Role         = "normal",
            ChatGp       = "(A0021)消防組",
            JoinedTeam   = joinedTeam,
            JoinedChatRoom = joinedChatRoom,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static string BuildTeamPayload() =>
        JsonSerializer.Serialize(new InviteMemberOutboxPayload(
            EventId:         "E20240101120000A001",
            MemberAccount:   "marry",
            OperatorAccount: "joseph",
            TargetSn:        99823104L,
            ChatGp:          null));

    private static string BuildChatPayload() =>
        JsonSerializer.Serialize(new InviteMemberOutboxPayload(
            EventId:         "E20240101120000A001",
            MemberAccount:   "marry",
            OperatorAccount: "joseph",
            TargetSn:        88723611L,
            ChatGp:          "(A0021)消防組"));

    // ── InviteTeamMember ──────────────────────────────────────────

    [Fact]
    public async Task InviteTeamMember_成功_應將JoinedTeam設為True()
    {
        var db = await SetupDbAsync(nameof(InviteTeamMember_成功_應將JoinedTeam設為True));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.InviteTeamMemberAsync(Arg.Any<InviteTeamMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        var handler = new InviteTeamMemberOutboxHandler(
            mockClient, db, NullLogger<InviteTeamMemberOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildTeamPayload(), CancellationToken.None);

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.JoinedTeam.Should().BeTrue();
    }

    [Fact]
    public async Task InviteTeamMember_JoinedTeam已True_應冪等跳過()
    {
        var db = await SetupDbAsync(
            nameof(InviteTeamMember_JoinedTeam已True_應冪等跳過),
            joinedTeam: true);

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        var handler    = new InviteTeamMemberOutboxHandler(
            mockClient, db, NullLogger<InviteTeamMemberOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildTeamPayload(), CancellationToken.None);

        await mockClient.DidNotReceive()
            .InviteTeamMemberAsync(Arg.Any<InviteTeamMemberRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteTeamMember_API失敗_應拋出例外()
    {
        var db = await SetupDbAsync(nameof(InviteTeamMember_API失敗_應拋出例外));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.InviteTeamMemberAsync(Arg.Any<InviteTeamMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(false, "Error", -1));

        var handler = new InviteTeamMemberOutboxHandler(
            mockClient, db, NullLogger<InviteTeamMemberOutboxHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildTeamPayload(), CancellationToken.None));
    }

    // ── InviteChatMember ──────────────────────────────────────────

    [Fact]
    public async Task InviteChatMember_成功_應將JoinedChatRoom設為True()
    {
        var db = await SetupDbAsync(nameof(InviteChatMember_成功_應將JoinedChatRoom設為True));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.InviteChatMemberAsync(Arg.Any<InviteChatMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        var handler = new InviteChatMemberOutboxHandler(
            mockClient, db, NullLogger<InviteChatMemberOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildChatPayload(), CancellationToken.None);

        var responder = await db.EventResponders
            .FirstAsync(r => r.Account == "marry");
        responder.JoinedChatRoom.Should().BeTrue();
    }

    [Fact]
    public async Task InviteChatMember_JoinedChatRoom已True_應冪等跳過()
    {
        var db = await SetupDbAsync(
            nameof(InviteChatMember_JoinedChatRoom已True_應冪等跳過),
            joinedChatRoom: true);

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        var handler    = new InviteChatMemberOutboxHandler(
            mockClient, db, NullLogger<InviteChatMemberOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildChatPayload(), CancellationToken.None);

        await mockClient.DidNotReceive()
            .InviteChatMemberAsync(Arg.Any<InviteChatMemberRequest>(), Arg.Any<CancellationToken>());
    }
}
