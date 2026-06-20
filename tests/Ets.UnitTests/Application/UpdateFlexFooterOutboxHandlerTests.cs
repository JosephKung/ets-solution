// tests/Ets.UnitTests/Application/UpdateFlexFooterOutboxHandlerTests.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Infrastructure.Outbox.Handlers;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Application;

public sealed class UpdateFlexFooterOutboxHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static string BuildPayloadJson(
        string footerText = "已送出！",
        string fontColor  = "#E53935") =>
        JsonSerializer.Serialize(new UpdateFlexFooterOutboxPayload(
            EventId:    "E20240101120000A001",
            EventType:  "a",
            MessageSn:  9453,
            Recipient:  "marry",
            FooterText: footerText,
            FontColor:  fontColor));

    // ─── 測試 1：成功更新 Footer ──────────────────────────────────
    [Fact]
    public async Task HandleAsync_成功_應呼叫UpdateFlexFooterAsync()
    {
        var db         = CreateDb(nameof(HandleAsync_成功_應呼叫UpdateFlexFooterAsync));
        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        mockClient.UpdateFlexFooterAsync(
                Arg.Any<UpdateFlexFooterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        var handler = new UpdateFlexFooterOutboxHandler(
            mockClient, db, NullLogger<UpdateFlexFooterOutboxHandler>.Instance);

        await handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None);

        await mockClient.Received(1).UpdateFlexFooterAsync(
            Arg.Is<UpdateFlexFooterRequest>(r =>
                r.Recipient   == "marry"   &&
                r.MessageSN   == 9453      &&
                r.FooterText  == "已送出！" &&
                r.FontColor   == "#E53935"),
            Arg.Any<CancellationToken>());
    }

    // ─── 測試 2：API 失敗應拋出例外 ──────────────────────────────
    [Fact]
    public async Task HandleAsync_API失敗_應拋出例外()
    {
        var db         = CreateDb(nameof(HandleAsync_API失敗_應拋出例外));
        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        mockClient.UpdateFlexFooterAsync(
                Arg.Any<UpdateFlexFooterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(false, "Error", -1));

        var handler = new UpdateFlexFooterOutboxHandler(
            mockClient, db, NullLogger<UpdateFlexFooterOutboxHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(1L, BuildPayloadJson(), CancellationToken.None));
    }

    // ─── 測試 3：CannotArrive 應使用灰色 ─────────────────────────
    [Fact]
    public async Task HandleAsync_CannotArrive_應使用灰色Footer()
    {
        var db         = CreateDb(nameof(HandleAsync_CannotArrive_應使用灰色Footer));
        var mockClient = Substitute.For<ITeamPlusChannelClient>();
        mockClient.UpdateFlexFooterAsync(
                Arg.Any<UpdateFlexFooterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        var handler = new UpdateFlexFooterOutboxHandler(
            mockClient, db, NullLogger<UpdateFlexFooterOutboxHandler>.Instance);

        var (footerText, fontColor) = FooterTextHelper.GetFooter("無法返回院區");
        await handler.HandleAsync(
            1L, BuildPayloadJson(footerText, fontColor), CancellationToken.None);

        await mockClient.Received(1).UpdateFlexFooterAsync(
            Arg.Is<UpdateFlexFooterRequest>(r =>
                r.FontColor  == FooterTextHelper.ColorGray &&
                r.FooterText.Contains("無法返回")),
            Arg.Any<CancellationToken>());
    }

    // ─── 測試 4：FooterTextHelper 各 ReplyStatus 對應測試 ────────
    [Theory]
    [InlineData("15 分鐘內",      "已送出！",              "#E53935")]
    [InlineData("30 分鐘內",      "已送出！",              "#E53935")]
    [InlineData("無法返回院區",   "已送出！您已表示無法返回。", "#888888")]
    [InlineData("VoiceConfirmed", "語音已送達!請開 team+ 回覆", "#888888")]
    [InlineData("VoiceUnreachable","語音通報未接通",        "#888888")]
    public void FooterTextHelper_各ReplyStatus_應回傳正確文字與顏色(
        string replyStatus, string expectedText, string expectedColor)
    {
        var (footerText, fontColor) = FooterTextHelper.GetFooter(replyStatus);

        footerText.Should().Be(expectedText);
        fontColor.Should().Be(expectedColor);
    }
}
