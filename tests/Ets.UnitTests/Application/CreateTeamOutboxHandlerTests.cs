// tests/Ets.UnitTests/Application/CreateTeamOutboxHandlerTests.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
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

public sealed class CreateTeamOutboxHandlerTests
{
    // ─── In-Memory DB ─────────────────────────────────────────────
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static string BuildPayloadJson(string eventId = "E20240101120000A001") =>
        JsonSerializer.Serialize(new CreateTeamOutboxPayload(
            EventId:           eventId,
            EventType:         "a",
            CommanderAccounts: ["joseph", "peter"],
            TeamName:          "XX醫療大樓火警警報團隊",
            Subject:           "處理XX醫療大樓火警警報",
            Description:       "台北市XX醫療大樓發生火警",
            MemberAccounts:    ["joseph", "peter", "marry"],
            ManagerAccounts:   ["joseph", "peter"]));

    // ─── 測試 1：成功建立團隊，回填 TeamPlusBigTeamSn ────────────
    [Fact]
    public async Task HandleAsync_成功建立_應回填TeamSn並插入APIAccountOutbox()
    {
        // Arrange
        var db = CreateDb(nameof(HandleAsync_成功建立_應回填TeamSn並插入APIAccountOutbox));

        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventSummary = "火警",
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.CreateTeamAsync(Arg.Any<CreateTeamRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTeamResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                TeamSN: 99823104L,
                IgnoredMemberList: [],
                IgnoredManagerList: []));

        var handler = new CreateTeamOutboxHandler(
            mockClient, db, NullLogger<CreateTeamOutboxHandler>.Instance);

        // Act
        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        // Assert：TeamPlusBigTeamSn 已回填
        var ev = await db.EmergencyEvents.FindAsync("E20240101120000A001");
        ev!.TeamPlusBigTeamSn.Should().Be(99823104);

        // Assert：CreateTeamAPIAccount Outbox 已插入
        var outbox = await db.OutboxMessages
            .FirstOrDefaultAsync(o => o.MessageType == OutboxMessageType.CreateTeamAPIAccount);
        outbox.Should().NotBeNull();
        outbox!.EventId.Should().Be("E20240101120000A001");
    }

    // ─── 測試 2：冪等性 — TeamSn 已存在時跳過 ────────────────────
    [Fact]
    public async Task HandleAsync_TeamSn已存在_應冪等跳過不重複呼叫API()
    {
        // Arrange
        var db = CreateDb(nameof(HandleAsync_TeamSn已存在_應冪等跳過不重複呼叫API));

        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId           = "E20240101120000A001",
            EventType         = "a",
            EventSummary      = "火警",
            TeamPlusBigTeamSn = 99999,   // 已有 TeamSn
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        var handler    = new CreateTeamOutboxHandler(
            mockClient, db, NullLogger<CreateTeamOutboxHandler>.Instance);

        // Act
        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        // Assert：API 不應被呼叫
        await mockClient.DidNotReceive()
            .CreateTeamAsync(Arg.Any<CreateTeamRequest>(), Arg.Any<CancellationToken>());
    }

    // ─── 測試 3：API 失敗應拋出例外（觸發 Outbox 重試）──────────
    [Fact]
    public async Task HandleAsync_API失敗_應拋出例外觸發Outbox重試()
    {
        // Arrange
        var db = CreateDb(nameof(HandleAsync_API失敗_應拋出例外觸發Outbox重試));

        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventSummary = "火警",
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.CreateTeamAsync(Arg.Any<CreateTeamRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTeamResult(
                IsSuccess: false, Description: "Server Error", ErrorCode: -1,
                TeamSN: 0, IgnoredMemberList: [], IgnoredManagerList: []));

        var handler = new CreateTeamOutboxHandler(
            mockClient, db, NullLogger<CreateTeamOutboxHandler>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None));
    }

    // ─── 測試 4：IgnoredMemberList 非空時寫 AuditLog ─────────────
    [Fact]
    public async Task HandleAsync_有IgnoredMember_應寫AuditLog()
    {
        // Arrange
        var db = CreateDb(nameof(HandleAsync_有IgnoredMember_應寫AuditLog));

        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventSummary = "火警",
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.CreateTeamAsync(Arg.Any<CreateTeamRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTeamResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                TeamSN: 99823104L,
                IgnoredMemberList: ["ghost_user"],
                IgnoredManagerList: []));

        var handler = new CreateTeamOutboxHandler(
            mockClient, db, NullLogger<CreateTeamOutboxHandler>.Instance);

        // Act
        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        // Assert：AuditLog 已寫入
        var auditLog = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "CreateTeam_IgnoredMember");
        auditLog.Should().NotBeNull();
        auditLog!.Detail.Should().Contain("ghost_user");
    }
}
