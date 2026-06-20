// tests/Ets.UnitTests/Application/AssignManagerOutboxHandlerTests.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
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

public sealed class AssignManagerOutboxHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static string BuildPayloadJson(long targetSn = 99823104L) =>
        JsonSerializer.Serialize(new AssignManagerOutboxPayload(
            EventId:         "E20240101120000A001",
            ManagerAccount:  "marry",
            OperatorAccount: "joseph",
            TargetSn:        targetSn));

    // ── AssignTeamManager ─────────────────────────────────────────

    [Fact]
    public async Task AssignTeamManager_成功_不應拋出例外()
    {
        var db = CreateDb(nameof(AssignTeamManager_成功_不應拋出例外));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.AssignTeamManagerAsync(
                Arg.Any<AssignManagerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        var handler = new AssignTeamManagerOutboxHandler(
            mockClient, db, NullLogger<AssignTeamManagerOutboxHandler>.Instance);

        var act = async () => await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);
        await act.Should().NotThrowAsync();

        await mockClient.Received(1)
            .AssignTeamManagerAsync(
                Arg.Is<AssignManagerRequest>(r =>
                    r.ManagerList.Contains("marry") &&
                    r.OperatorAccount == "joseph" &&
                    r.TeamOrChatSN == 99823104L),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignTeamManager_API失敗_應拋出例外()
    {
        var db = CreateDb(nameof(AssignTeamManager_API失敗_應拋出例外));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.AssignTeamManagerAsync(
                Arg.Any<AssignManagerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(false, "Error", -1));

        var handler = new AssignTeamManagerOutboxHandler(
            mockClient, db, NullLogger<AssignTeamManagerOutboxHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None));
    }

    // ── AssignChatManager ─────────────────────────────────────────

    [Fact]
    public async Task AssignChatManager_成功_不應拋出例外()
    {
        var db = CreateDb(nameof(AssignChatManager_成功_不應拋出例外));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.AssignChatManagerAsync(
                Arg.Any<AssignManagerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        var handler = new AssignChatManagerOutboxHandler(
            mockClient, db, NullLogger<AssignChatManagerOutboxHandler>.Instance);

        var act = async () => await handler.HandleAsync(
            1L, BuildPayloadJson(88723611L), CancellationToken.None);
        await act.Should().NotThrowAsync();

        await mockClient.Received(1)
            .AssignChatManagerAsync(
                Arg.Is<AssignManagerRequest>(r =>
                    r.ManagerList.Contains("marry") &&
                    r.TeamOrChatSN == 88723611L),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignChatManager_API失敗_應拋出例外()
    {
        var db = CreateDb(nameof(AssignChatManager_API失敗_應拋出例外));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.AssignChatManagerAsync(
                Arg.Any<AssignManagerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(false, "Error", -1));

        var handler = new AssignChatManagerOutboxHandler(
            mockClient, db, NullLogger<AssignChatManagerOutboxHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildPayloadJson(88723611L), CancellationToken.None));
    }
}
