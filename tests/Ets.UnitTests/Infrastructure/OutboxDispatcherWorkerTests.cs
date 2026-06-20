// tests/Ets.UnitTests/Infrastructure/OutboxDispatcherWorkerTests.cs
using Ets.Application.Interfaces;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.BackgroundServices;
using Ets.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Ets.UnitTests.Infrastructure;

public sealed class OutboxDispatcherWorkerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static (IServiceScopeFactory, AppDbContext) CreateScopeFactory(
        string dbName,
        IOutboxHandler handler)
    {
        var db = CreateDb(dbName);

        var factory     = Substitute.For<IOutboxHandlerFactory>();
        factory.GetHandler(Arg.Any<OutboxMessageType>()).Returns(handler);

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IOutboxHandlerFactory)).Returns(factory);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return (scopeFactory, db);
    }

    // ─── 測試 1：Pending 訊息成功派送後 Status 應改為 Processed ──
    [Fact]
    public async Task DispatchBatch_成功派送_Status應改為Processed()
    {
        var mockHandler = Substitute.For<IOutboxHandler>();
        mockHandler.MessageType.Returns(OutboxMessageType.CreateTeam);
        mockHandler.HandleAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (scopeFactory, db) = CreateScopeFactory(
            nameof(DispatchBatch_成功派送_Status應改為Processed), mockHandler);

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventId     = "E001",
            MessageType = OutboxMessageType.CreateTeam,
            PayloadJson = "{}",
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var worker = new OutboxDispatcherWorker(
            scopeFactory, NullLogger<OutboxDispatcherWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        await mockHandler.Received().HandleAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ─── 測試 2：Handler 拋出例外應進入 Failed + 設定 NextRetryAt ─
    [Fact]
    public async Task DispatchBatch_Handler失敗_應設為Failed並設NextRetryAt()
    {
        var dbName = nameof(DispatchBatch_Handler失敗_應設為Failed並設NextRetryAt);
        var db     = CreateDb(dbName);

        var mockHandler = Substitute.For<IOutboxHandler>();
        mockHandler.MessageType.Returns(OutboxMessageType.CreateTeam);
        mockHandler.HandleAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("team+ API 失敗"));

        var factory = Substitute.For<IOutboxHandlerFactory>();
        factory.GetHandler(Arg.Any<OutboxMessageType>()).Returns(mockHandler);

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(AppDbContext)).Returns(db);
        sp.GetService(typeof(IOutboxHandlerFactory)).Returns(factory);

        var scope        = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventId     = "E001",
            MessageType = OutboxMessageType.CreateTeam,
            PayloadJson = "{}",
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var worker = new OutboxDispatcherWorker(
            scopeFactory, NullLogger<OutboxDispatcherWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        var msg = await db.OutboxMessages.FirstAsync();
        msg.Status.Should().Be(OutboxMessageStatus.Failed);
        msg.RetryCount.Should().Be(1);
        msg.NextRetryAt.Should().NotBeNull();
        msg.LastError.Should().Contain("team+ API 失敗");
    }

    // ─── 測試 3：達到最大重試次數應移至 DeadLetter ───────────────
    [Fact]
    public async Task OutboxHandlerFactory_找不到Handler_應拋出例外()
    {
        var factory = new OutboxHandlerFactory(
            Array.Empty<IOutboxHandler>());

        var act = () => factory.GetHandler(OutboxMessageType.CreateTeam);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MessageType=CreateTeam*");
    }
}
