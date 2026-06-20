// tests/Ets.UnitTests/Application/VirtualAccountOutboxHandlerTests.cs
using System.Security.Cryptography;
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Outbox.Handlers;
using Ets.Infrastructure.Persistence;
using Ets.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Application;

public sealed class VirtualAccountOutboxHandlerTests : IDisposable
{
    // ─── 測試用 Encryptor（設定隨機金鑰）────────────────────────
    private readonly string _originalEnvValue;
    private readonly IVirtualAccountKeyEncryptor _encryptor;

    public VirtualAccountOutboxHandlerTests()
    {
        _originalEnvValue = Environment.GetEnvironmentVariable(
            VirtualAccountKeyEncryptor.EnvKeyName) ?? string.Empty;

        var testKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable(VirtualAccountKeyEncryptor.EnvKeyName, testKey);

        _encryptor = new VirtualAccountKeyEncryptor(
            NullLogger<VirtualAccountKeyEncryptor>.Instance);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            VirtualAccountKeyEncryptor.EnvKeyName,
            string.IsNullOrEmpty(_originalEnvValue) ? null : _originalEnvValue);
    }

    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<AppDbContext> SetupDbAsync(
        string dbName,
        string? existingVirtualAccount = null,
        string? existingBatchId = null)
    {
        var db = CreateDb(dbName);
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId                = "E20240101120000A001",
            EventType              = "a",
            EventSummary           = "XX醫療大樓火警警報",
            EventTime              = new DateTime(2024, 1, 1, 12, 0, 0),
            EventDescription       = "台北市XX醫療大樓發生火警",
            TeamPlusBigTeamSn      = 99823104,
            TeamPlusVirtualAccount = existingVirtualAccount,
            TeamPlusArticleBatchId = existingBatchId,
            CreatedAt              = DateTime.UtcNow,
            UpdatedAt              = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    // ── CreateTeamAPIAccount ──────────────────────────────────────

    [Fact]
    public async Task CreateTeamApiAccount_成功_應回填VirtualAccount並加密ApiKey並排入PostVirtualMsg()
    {
        var db = await SetupDbAsync(
            nameof(CreateTeamApiAccount_成功_應回填VirtualAccount並加密ApiKey並排入PostVirtualMsg));

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.CreateTeamApiAccountAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateTeamApiAccountResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                ApiAccount: "team_99823104_bot_xyz",
                ApiKey:     "9d12abef-aaaa-bbbb-cccc-1234567890ab"));

        var handler = new CreateTeamApiAccountOutboxHandler(
            mockClient,
            _encryptor,
            db,
            NullLogger<CreateTeamApiAccountOutboxHandler>.Instance);

        var payload = JsonSerializer.Serialize(new CreateTeamApiAccountOutboxPayload(
            EventId:      "E20240101120000A001",
            TeamSn:       99823104L,
            OwnerAccount: "joseph"));

        await handler.HandleAsync(1L, payload, CancellationToken.None);

        var ev = await db.EmergencyEvents.FindAsync("E20240101120000A001");
        ev!.TeamPlusVirtualAccount.Should().Be("team_99823104_bot_xyz");

        // ApiKey 應已加密寫入（byte[] 非空）
        ev.TeamPlusVirtualAccountApiKey.Should().NotBeNull();
        ev.TeamPlusVirtualAccountApiKey!.Length.Should().BeGreaterThan(0);

        // 解密後應還原原始值
        var decrypted = _encryptor.Decrypt(ev.TeamPlusVirtualAccountApiKey);
        decrypted.Should().Be("9d12abef-aaaa-bbbb-cccc-1234567890ab");

        // PostVirtualMsg Outbox 已排入
        var outbox = await db.OutboxMessages
            .FirstOrDefaultAsync(o => o.MessageType == OutboxMessageType.PostVirtualMsg);
        outbox.Should().NotBeNull();
        // Payload 不應包含明文 ApiKey
        outbox!.PayloadJson.Should().NotContain("9d12abef");
    }

    [Fact]
    public async Task CreateTeamApiAccount_VirtualAccount已存在_應冪等跳過()
    {
        var db = await SetupDbAsync(
            nameof(CreateTeamApiAccount_VirtualAccount已存在_應冪等跳過),
            existingVirtualAccount: "team_existing_bot");

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        var handler    = new CreateTeamApiAccountOutboxHandler(
            mockClient,
            _encryptor,
            db,
            NullLogger<CreateTeamApiAccountOutboxHandler>.Instance);

        var payload = JsonSerializer.Serialize(new CreateTeamApiAccountOutboxPayload(
            "E20240101120000A001", 99823104L, "joseph"));

        await handler.HandleAsync(1L, payload, CancellationToken.None);

        await mockClient.DidNotReceive()
            .CreateTeamApiAccountAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    // ── PostVirtualMsg ────────────────────────────────────────────

    [Fact]
    public async Task PostVirtualMsg_成功_應回填BatchId()
    {
        var db = await SetupDbAsync(
            nameof(PostVirtualMsg_成功_應回填BatchId),
            existingVirtualAccount: "team_99823104_bot_xyz");

        // 預先寫入加密的 ApiKey
        var ev = await db.EmergencyEvents.FindAsync("E20240101120000A001");
        ev!.TeamPlusVirtualAccountApiKey = _encryptor.Encrypt("9d12abef-test-key");
        await db.SaveChangesAsync();

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        mockClient.PostTeamMessageAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PostTeamMessageResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                BatchId: "post_a1b2c3d4"));

        var handler = new PostVirtualMsgOutboxHandler(
            mockClient,
            _encryptor,
            db,
            NullLogger<PostVirtualMsgOutboxHandler>.Instance);

        var payload = JsonSerializer.Serialize(new PostVirtualMsgOutboxPayload(
            EventId:     "E20240101120000A001",
            TeamSn:      99823104L,
            TextContent: "緊急應變智能通報\n2024/01/01 12:00\n事件類型：a 火警",
            Subject:     "a 火警事件記錄"));

        await handler.HandleAsync(2L, payload, CancellationToken.None);

        var evAfter = await db.EmergencyEvents.FindAsync("E20240101120000A001");
        evAfter!.TeamPlusArticleBatchId.Should().Be("post_a1b2c3d4");
    }

    [Fact]
    public async Task PostVirtualMsg_BatchId已存在_應冪等跳過()
    {
        var db = await SetupDbAsync(
            nameof(PostVirtualMsg_BatchId已存在_應冪等跳過),
            existingBatchId: "post_existing");

        var mockClient = Substitute.For<ITeamPlusSystemClient>();
        var handler    = new PostVirtualMsgOutboxHandler(
            mockClient,
            _encryptor,
            db,
            NullLogger<PostVirtualMsgOutboxHandler>.Instance);

        var payload = JsonSerializer.Serialize(new PostVirtualMsgOutboxPayload(
            "E20240101120000A001", 99823104L, "內容", "主旨"));

        await handler.HandleAsync(2L, payload, CancellationToken.None);

        await mockClient.DidNotReceive()
            .PostTeamMessageAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
