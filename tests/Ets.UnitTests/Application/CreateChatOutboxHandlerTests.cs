// tests/Ets.UnitTests/Application/CreateChatOutboxHandlerTests.cs
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

public sealed class CreateChatOutboxHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static string BuildPayloadJson(
        long groupId = 1L,
        string chatGp = "(A0021)消防組",
        int? splitGroupIndex = null) =>
        JsonSerializer.Serialize(new CreateChatOutboxPayload(
            EventId:          "E20240101120000A001",
            GroupId:          groupId,
            ChatGp:           chatGp,
            CreatorAccount:   "joseph",
            MemberAccounts:   ["joseph"],
            ManagerAccounts:  ["joseph"],
            SplitGroupIndex:  splitGroupIndex));

    private static async Task<AppDbContext> SetupDbWithGroupAsync(
        string dbName, long groupId, int? existingChatSn = null)
    {
        var db = CreateDb(dbName);
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventSummary = "火警",
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        db.EventGroups.Add(new EventGroup
        {
            GroupId          = groupId,
            EventId          = "E20240101120000A001",
            ChatGp           = "(A0021)消防組",
            TeamPlusChatSn   = existingChatSn,
            CreatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    // ─── 測試 1：成功建立交談室，回填 TeamPlusChatSn ──────────────
    [Fact]
    public async Task HandleAsync_成功建立_應回填TeamPlusChatSn()
    {
        // Arrange
        var db = await SetupDbWithGroupAsync(
            nameof(HandleAsync_成功建立_應回填TeamPlusChatSn), groupId: 1L);

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.CreateChatAsync(Arg.Any<CreateChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateChatResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                ChatSN: 88723611L,
                IgnoredMemberList: [],
                IgnoredManagerList: []));

        var handler = new CreateChatOutboxHandler(
            mockClient, db, NullLogger<CreateChatOutboxHandler>.Instance);

        // Act
        await handler.HandleAsync(1L, BuildPayloadJson(groupId: 1L), CancellationToken.None);

        // Assert
        var group = await db.EventGroups.FindAsync(1L);
        group!.TeamPlusChatSn.Should().Be(88723611);
        group.CreatorAccount.Should().Be("joseph");
        group.MemberCount.Should().Be(1);
    }

    // ─── 測試 2：冪等性 — ChatSn 已存在時跳過 ────────────────────
    [Fact]
    public async Task HandleAsync_ChatSn已存在_應冪等跳過()
    {
        // Arrange
        var db = await SetupDbWithGroupAsync(
            nameof(HandleAsync_ChatSn已存在_應冪等跳過),
            groupId: 1L, existingChatSn: 99999);

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        var handler    = new CreateChatOutboxHandler(
            mockClient, db, NullLogger<CreateChatOutboxHandler>.Instance);

        // Act
        await handler.HandleAsync(1L, BuildPayloadJson(groupId: 1L), CancellationToken.None);

        // Assert：API 不應被呼叫
        await mockClient.DidNotReceive()
            .CreateChatAsync(Arg.Any<CreateChatRequest>(), Arg.Any<CancellationToken>());
    }

    // ─── 測試 3：API 失敗應拋出例外（觸發 Outbox 重試）──────────
    [Fact]
    public async Task HandleAsync_API失敗_應拋出例外()
    {
        // Arrange
        var db = await SetupDbWithGroupAsync(
            nameof(HandleAsync_API失敗_應拋出例外), groupId: 1L);

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.CreateChatAsync(Arg.Any<CreateChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateChatResult(
                IsSuccess: false, Description: "Server Error", ErrorCode: -1,
                ChatSN: 0, IgnoredMemberList: [], IgnoredManagerList: []));

        var handler = new CreateChatOutboxHandler(
            mockClient, db, NullLogger<CreateChatOutboxHandler>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildPayloadJson(groupId: 1L), CancellationToken.None));
    }

    // ─── 測試 4：分流名稱產生邏輯 ────────────────────────────────
    [Theory]
    [InlineData("(A0021)消防組",    null, "(A0021)消防組")]         // 無分流，9字，不截斷
    [InlineData("(A0021)消防組",    1,    "(A0021)消防組-1")]       // 分流 1，9+2=11字，不截斷
    [InlineData("(A0021)消防組",    2,    "(A0021)消防組-2")]       // 分流 2
    [InlineData("ABCDEFGHIJKLMNOPQRST", null, "ABCDEFGHIJKLMNOPQRST")]  // 正好 20 字
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", null, "ABCDEFGHIJKLMNOPQRST")] // 21 字，截斷為 20
    [InlineData("ABCDEFGHIJKLMNOPQRST", 1,    "ABCDEFGHIJKLMNOPQR-1")] // 20 字 + 分流，截斷 base 為 18
    public void BuildChatName_各情境_應產生正確名稱(
        string chatGp, int? splitIndex, string expected)
    {
        var result = CreateChatOutboxHandler.BuildChatName(chatGp, splitIndex);
        result.Should().Be(expected);
        result.Length.Should().BeLessThanOrEqualTo(20);
    }
}
