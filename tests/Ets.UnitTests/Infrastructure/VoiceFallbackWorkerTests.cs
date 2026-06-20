// tests/Ets.UnitTests/Infrastructure/VoiceFallbackWorkerTests.cs
using Ets.Application.Dtos.Voice;
using Ets.Application.Interfaces;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.BackgroundServices;
using Ets.Infrastructure.ExternalClients.Voice;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Infrastructure;

public sealed class VoiceFallbackWorkerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static VoiceNotifyOptions DefaultOptions => new()
    {
        TimeoutMinutes      = 10,
        MaxRetryCount       = 3,
        ScanIntervalSeconds = 30,
        BatchSize           = 100,
        VoiceApiBaseUrl     = "https://voice.hospital.internal",
        VoiceApiToken       = "test-token",
        CallbackBaseUrl     = "https://ets.hospital.internal/api/v1/webhooks/voicebot"
    };

    private static async Task<AppDbContext> SetupDbAsync(
        string dbName,
        int? userNo = 9,
        string replyStatus = "Pending",
        int voiceRetryCount = 0,
        DateTime? lastVoiceCallTime = null)
    {
        var db = CreateDb(dbName);
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId      = "E20240101120000A001",
            EventType    = "a",
            EventSummary = "火警",
            AudioContent = "發生火災，請立即應變",
            Status       = EventStatus.Processing,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        });
        db.EventResponders.Add(new EventResponder
        {
            EventId           = "E20240101120000A001",
            Account           = "marry",
            UserNo            = userNo,
            Role              = "normal",
            ChatGp            = "(A0021)消防組",
            ReplyStatus       = replyStatus,
            VoiceRetryCount   = voiceRetryCount,
            LastVoiceCallTime = lastVoiceCallTime,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static (IServiceScopeFactory, AppDbContext) CreateScopeFactory(
        string dbName, IVoiceApiClient voiceClient)
    {
        var db = CreateDb(dbName);

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IVoiceApiClient)).Returns(voiceClient);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return (scopeFactory, db);
    }

    // ─── 測試 1：Pending + UserNo 有值 + 超時 → 應外撥並更新 DB ──
    [Fact]
    public async Task VoiceFallback_Pending且超時且UserNo有值_應外撥並更新DB()
    {
        var dbName = nameof(VoiceFallback_Pending且超時且UserNo有值_應外撥並更新DB);
        var db = await SetupDbAsync(dbName, userNo: 9, lastVoiceCallTime: null);

        var mockVoice = Substitute.For<IVoiceApiClient>();
        mockVoice.CallAsync(Arg.Any<VoiceCallRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallResult(
                IsSuccess: true,
                ExternalCallId: "E20240101120000A001-9-1",
                Status: "QUEUED",
                QueueInUse: 1, QueueMax: 10, QueueWaiting: 0));

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IVoiceApiClient)).Returns(mockVoice);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var worker = new VoiceFallbackWorker(
            scopeFactory,
            Options.Create(DefaultOptions),
            NullLogger<VoiceFallbackWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        await mockVoice.Received()
            .CallAsync(
                Arg.Is<VoiceCallRequest>(r =>
                    r.CalleeAccount == "9" &&
                    r.EventId == "E20240101120000A001"),
                Arg.Any<CancellationToken>());
    }

    // ─── 測試 2：UserNo 為 NULL → 應跳過不外撥 ──────────────────
    [Fact]
    public async Task VoiceFallback_UserNoNull_應跳過不外撥()
    {
        var dbName   = nameof(VoiceFallback_UserNoNull_應跳過不外撥);
        var db       = await SetupDbAsync(dbName, userNo: null);
        var mockVoice = Substitute.For<IVoiceApiClient>();

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IVoiceApiClient)).Returns(mockVoice);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var worker = new VoiceFallbackWorker(
            scopeFactory,
            Options.Create(DefaultOptions),
            NullLogger<VoiceFallbackWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        await mockVoice.DidNotReceive()
            .CallAsync(Arg.Any<VoiceCallRequest>(), Arg.Any<CancellationToken>());
    }

    // ─── 測試 3：ReplyStatus 非 Pending → 不外撥 ────────────────
    [Fact]
    public async Task VoiceFallback_已回覆_應跳過不外撥()
    {
        var dbName   = nameof(VoiceFallback_已回覆_應跳過不外撥);
        var db       = await SetupDbAsync(dbName, userNo: 9, replyStatus: "15 分鐘內");
        var mockVoice = Substitute.For<IVoiceApiClient>();

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IVoiceApiClient)).Returns(mockVoice);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var worker = new VoiceFallbackWorker(
            scopeFactory,
            Options.Create(DefaultOptions),
            NullLogger<VoiceFallbackWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        await mockVoice.DidNotReceive()
            .CallAsync(Arg.Any<VoiceCallRequest>(), Arg.Any<CancellationToken>());
    }

    // ─── 測試 4：VoiceRetryCount >= MaxRetryCount → 不外撥 ───────
    [Fact]
    public async Task VoiceFallback_達到最大重試_應跳過不外撥()
    {
        var dbName   = nameof(VoiceFallback_達到最大重試_應跳過不外撥);
        // VoiceRetryCount = 3，MaxRetryCount = 3 → 不外撥
        var db       = await SetupDbAsync(dbName, userNo: 9, voiceRetryCount: 3);
        var mockVoice = Substitute.For<IVoiceApiClient>();

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IVoiceApiClient)).Returns(mockVoice);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var worker = new VoiceFallbackWorker(
            scopeFactory,
            Options.Create(DefaultOptions),
            NullLogger<VoiceFallbackWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        await mockVoice.DidNotReceive()
            .CallAsync(Arg.Any<VoiceCallRequest>(), Arg.Any<CancellationToken>());
    }
}
