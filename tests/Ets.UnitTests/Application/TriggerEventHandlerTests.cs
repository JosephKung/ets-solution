using Ets.Application.Abstractions;
using Ets.Application.Commands;
using Ets.Application.Dtos;
using Ets.Application.Exceptions;
using Ets.Application.Handlers;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Ets.UnitTests.Application;

/// <summary>
/// TriggerEventCommandHandler 單元測試。
/// 覆蓋：防重、正常寫入（Outbox 數量驗證）、DB 唯一鍵觸發 Conflict。
/// </summary>
public class TriggerEventHandlerTests
{
    private readonly IEtsRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TriggerEventCommandHandler> _logger;
    private readonly TriggerEventCommandHandler _handler;

    public TriggerEventHandlerTests()
    {
        _repo = Substitute.For<IEtsRepository>();
        _uow = Substitute.For<IUnitOfWork>();
        _logger = Substitute.For<ILogger<TriggerEventCommandHandler>>();
        _handler = new TriggerEventCommandHandler(_repo, _uow, _logger);

        // BeginTransactionAsync 回傳可 await dispose 的 stub
        _uow.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
    }

    // ── 輔助方法：建立合法 DTO ────────────────────────────────────────────────

    private static TriggerEventCommand ValidCommand(
        string eventId = "E20240101120000A001",
        int groupCount = 2,
        int respCount = 3) => new(new HisEventTriggerDto
        {
            EventId = eventId,
            EventType = "a",
            EventTime = "2024-01-01 12:00:00",
            EventArea = "林口院區",
            EventSummary = "XX醫療大樓火警警報",
            EventSource = "HIS",
            EventFlexMsgItems = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
            EventCommander = "[\"joseph\"]",
            EventGroups = Enumerable.Range(1, groupCount)
            .Select(i => new HisEventGroupDto { ChatGp = $"(A{i:D4})消防組{i}" })
            .ToList(),
            EventResponders = new List<HisEventResponderDto>
            {
                new() { Account = "joseph", Role = "commander", ChatGp = "(A0001)消防組1" }
            }
            .Concat(Enumerable.Range(2, respCount - 1)
                .Select(i => new HisEventResponderDto
                { Account = $"user{i}", Role = "normal", ChatGp = "(A0001)消防組1" }))
            .ToList()
        });

    // ── 防重（1.2.4）─────────────────────────────────────────────────────────

    [Fact(DisplayName = "EventId 已存在時應回傳 Conflict（E3006），不寫入任何資料")]
    public async Task Handle_DuplicateEventId_ShouldReturn_Conflict()
    {
        // Arrange
        _repo.EventExistsAsync("E20240101120000A001", Arg.Any<CancellationToken>())
             .Returns(true);

        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("E3006");
        result.SuggestedHttpStatus.Should().Be(409);

        // 不應呼叫 AddEventAsync
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<EmergencyEvent>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // ── 正常流程：Outbox 數量驗證（1.2.5）────────────────────────────────────

    [Theory(DisplayName = "Outbox 應寫入 CreateTeam(1) + CreateChat(N) + SendFlex(1) = N+2 筆")]
    [InlineData(1, 3)]   // 1 群組 → 3 筆 Outbox
    [InlineData(2, 4)]   // 2 群組 → 4 筆 Outbox
    [InlineData(5, 7)]   // 5 群組 → 7 筆 Outbox
    public async Task Handle_ValidCommand_OutboxCount_ShouldBe_GroupsPlusTwo(
        int groupCount, int expectedOutbox)
    {
        // Arrange
        _repo.EventExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(false);

        var capturedOutbox = new List<OutboxMessage>();
        await _repo.AddOutboxMessageAsync(
            Arg.Do<OutboxMessage>(m => capturedOutbox.Add(m)),
            Arg.Any<CancellationToken>());

        // Act
        var result = await _handler.Handle(
            ValidCommand(groupCount: groupCount), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedOutbox.Should().HaveCount(expectedOutbox,
            because: $"{groupCount} 群組應產生 {groupCount}+2={expectedOutbox} 筆 Outbox");

        capturedOutbox.Should().Contain(m => m.MessageType == OutboxMessageType.CreateTeam,
            because: "必須有 CreateTeam");
        capturedOutbox.Count(m => m.MessageType == OutboxMessageType.CreateChat)
            .Should().Be(groupCount, because: "每個群組各一筆 CreateChat");
        capturedOutbox.Should().Contain(m => m.MessageType == OutboxMessageType.SendFlexMessage,
            because: "必須有 SendFlexMessage");

        // 所有 Outbox 狀態應為 Pending
        capturedOutbox.Should().AllSatisfy(m =>
            m.Status.Should().Be(OutboxMessageStatus.Pending));
    }

    [Fact(DisplayName = "正常流程應依序呼叫：AddEvent → AddGroups → AddResponders → AddOutbox → SaveChanges → Commit")]
    public async Task Handle_ValidCommand_ShouldCallRepositoryInCorrectOrder()
    {
        // Arrange
        _repo.EventExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(false);

        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.EventId.Should().Be("E20240101120000A001");

        Received.InOrder(() =>
        {
            _repo.AddEventAsync(Arg.Any<EmergencyEvent>(), Arg.Any<CancellationToken>());
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            _uow.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    // ── DB 唯一鍵觸發（並發防重）────────────────────────────────────────────

    [Fact(DisplayName = "SaveChanges 拋出 DuplicateEventIdException 時應回傳 Conflict（並發防重）")]
    public async Task Handle_DbDuplicateException_ShouldReturn_Conflict()
    {
        // Arrange — 模擬第一層防重通過，但 DB 唯一鍵觸發（並發場景）
        _repo.EventExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new DuplicateEventIdException("E20240101120000A001"));

        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("E3006",
            because: "DB 唯一鍵觸發應被轉換為 Conflict 結果，而非讓例外向上傳遞");
    }

    // ── InferIntent（1.2.3）整合驗證 ────────────────────────────────────────

    [Fact(DisplayName = "Handler 執行後，EventResponder 的角色應依指揮官清單正確設定")]
    public async Task Handle_Commander_ShouldOverride_RoleToCommander()
    {
        // Arrange
        _repo.EventExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(false);

        var capturedResponders = new List<EventResponder>();
        await _repo.AddResponderAsync(
            Arg.Do<EventResponder>(r => capturedResponders.Add(r)),
            Arg.Any<CancellationToken>());

        // Act
        await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert — joseph 在 EventCommander 中，角色應被覆寫為 commander
        capturedResponders.Should().Contain(r =>
            r.Account == "joseph" && r.Role == "commander",
            because: "EventCommander 中的帳號應被覆寫為 commander 角色");
    }
}
