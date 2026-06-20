using Ets.Application.Abstractions;
using Ets.Application.Dtos.CheckIn;
using Ets.Application.Interfaces;
using Ets.Application.UseCases.CheckIn;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ets.UnitTests.Application;

public class CheckInUseCaseTests
{
    // ── 常數 ─────────────────────────────────────────────────────────────
    private const string TestHmacKey = "test-hmac-key-for-unit-test-only";
    private const string TestEventId = "E20240101120000A001";
    private const string TestAccount = "marry";

    /// <summary>產生符合驗證邏輯的 HMAC 簽章</summary>
    private static string ComputeSig(string nonce, DateTime expiresAt)
    {
        var expUnix = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var message = $"{nonce}|{expUnix}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(TestHmacKey));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant()[..8];
    }

    private (CheckInUseCase sut,
             IEtsRepository repo,
             IUnitOfWork uow,
             IDashboardNotifier notifier) BuildSut()
    {
        var repo     = Substitute.For<IEtsRepository>();
        var uow      = Substitute.For<IUnitOfWork>();
        var notifier = Substitute.For<IDashboardNotifier>();
        var keyProv  = Substitute.For<IQrHmacKeyProvider>();
        keyProv.GetKey().Returns(TestHmacKey);

        var sut = new CheckInUseCase(
            repo, uow, notifier,
            NullLogger<CheckInUseCase>.Instance,
            keyProv);

        return (sut, repo, uow, notifier);
    }

    private static QrCheckInToken ValidToken(string nonce, DateTime? expiresAt = null)
    {
        var exp = expiresAt ?? DateTime.UtcNow.AddMinutes(5);
        return new QrCheckInToken
        {
            TokenId   = Guid.NewGuid(),
            EventId   = TestEventId,
            Nonce     = nonce,
            Signature = ComputeSig(nonce, exp),
            IssuedAt  = DateTime.UtcNow,
            ExpiresAt = exp,
            IsActive  = true
        };
    }

    private static EventResponder ValidResponder() => new()
    {
        ResponderId    = 1,
        EventId        = TestEventId,
        Account        = TestAccount,
        CheckInStatus  = false,
        JoinedTeam     = true,
        JoinedChatRoom = true,
        ChatGp         = "消防組",
        Role           = "normal",
        UpdatedAt      = DateTime.UtcNow
    };

    private static EmergencyEvent ActiveEvent() => new()
    {
        EventId = TestEventId,
        Status  = 0
    };

    // ── 正向測試 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var (sut, repo, uow, notifier) = BuildSut();
        var nonce  = new string('a', 64);
        var token  = ValidToken(nonce);
        var request = new CheckInRequest
        {
            EventId = TestEventId, Account = TestAccount,
            Nonce   = nonce, Sig = ComputeSig(nonce, token.ExpiresAt)
        };

        repo.FindQrTokenByNonceAsync(nonce, default).Returns(token);
        repo.FindEventByIdAsync(TestEventId, default).Returns(ActiveEvent());
        repo.FindResponderByEventAndAccountAsync(TestEventId, TestAccount, default)
            .Returns(ValidResponder());

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.CheckedInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.AddedToTeam.Should().BeFalse();       // JoinedTeam=true → 不補位
        result.AddedToChatRoom.Should().BeFalse();   // JoinedChatRoom=true → 不補位
        await uow.Received(1).SaveChangesAsync(default);
        await notifier.Received(1).NotifyCheckInAsync(
            TestEventId, TestAccount, Arg.Any<DateTime>(), default);
    }

    [Fact]
    public async Task ExecuteAsync_JoinedTeamFalse_AddsInviteTeamMemberOutbox()
    {
        // Arrange
        var (sut, repo, _, _) = BuildSut();
        var nonce     = new string('b', 64);
        var token     = ValidToken(nonce);
        var responder = ValidResponder();
        responder.JoinedTeam = false;   // 尚未加入團隊

        repo.FindQrTokenByNonceAsync(nonce, default).Returns(token);
        repo.FindEventByIdAsync(TestEventId, default).Returns(ActiveEvent());
        repo.FindResponderByEventAndAccountAsync(TestEventId, TestAccount, default)
            .Returns(responder);

        // Act
        var result = await sut.ExecuteAsync(new CheckInRequest
        {
            EventId = TestEventId, Account = TestAccount,
            Nonce = nonce, Sig = ComputeSig(nonce, token.ExpiresAt)
        });

        // Assert
        result.AddedToTeam.Should().BeTrue();
        await repo.Received(1).AddOutboxMessageAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == OutboxMessageType.InviteTeamMember),
            default);
    }

    // ── 錯誤情境 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NonceNotFound_ThrowsC4001()
    {
        // Arrange
        var (sut, repo, _, _) = BuildSut();
        repo.FindQrTokenByNonceAsync(Arg.Any<string>(), default)
            .Returns((QrCheckInToken?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CheckInException>(
            () => sut.ExecuteAsync(new CheckInRequest
            {
                EventId = TestEventId, Account = TestAccount,
                Nonce = "nonexistent", Sig = "00000000"
            }));
        ex.ErrorCode.Should().Be("C4001");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidSignature_ThrowsC4002()
    {
        // Arrange
        var (sut, repo, _, _) = BuildSut();
        var nonce = new string('c', 64);
        repo.FindQrTokenByNonceAsync(nonce, default).Returns(ValidToken(nonce));
        repo.FindEventByIdAsync(TestEventId, default).Returns(ActiveEvent());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CheckInException>(
            () => sut.ExecuteAsync(new CheckInRequest
            {
                EventId = TestEventId, Account = TestAccount,
                Nonce = nonce, Sig = "badbadbad"
            }));
        ex.ErrorCode.Should().Be("C4002");
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredToken_ThrowsC4001()
    {
        // Arrange
        var (sut, repo, _, _) = BuildSut();
        var nonce = new string('d', 64);
        var expired = new QrCheckInToken
        {
            TokenId          = Guid.NewGuid(), EventId = TestEventId, Nonce = nonce,
            Signature        = "00000000",
            IssuedAt         = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAt        = DateTime.UtcNow.AddMinutes(-5),
            IsActive         = false,
            GracePeriodEndAt = DateTime.UtcNow.AddMinutes(-4)
        };
        repo.FindQrTokenByNonceAsync(nonce, default).Returns(expired);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CheckInException>(
            () => sut.ExecuteAsync(new CheckInRequest
            {
                EventId = TestEventId, Account = TestAccount,
                Nonce = nonce, Sig = ComputeSig(nonce, expired.ExpiresAt)
            }));
        ex.ErrorCode.Should().Be("C4001");
    }

    [Fact]
    public async Task ExecuteAsync_WithinGracePeriod_Succeeds()
    {
        // Arrange
        var (sut, repo, uow, _) = BuildSut();
        var nonce = new string('e', 64);
        var gracePeriodToken = new QrCheckInToken
        {
            TokenId          = Guid.NewGuid(), EventId = TestEventId, Nonce = nonce,
            Signature        = "00000000",
            IssuedAt         = DateTime.UtcNow.AddMinutes(-6),
            ExpiresAt        = DateTime.UtcNow.AddMinutes(-1),   // 已過期
            IsActive         = false,
            GracePeriodEndAt = DateTime.UtcNow.AddSeconds(30)    // 寬限期內
        };
        repo.FindQrTokenByNonceAsync(nonce, default).Returns(gracePeriodToken);
        repo.FindEventByIdAsync(TestEventId, default).Returns(ActiveEvent());
        repo.FindResponderByEventAndAccountAsync(TestEventId, TestAccount, default)
            .Returns(ValidResponder());

        // Act — 寬限期內應成功，不丟例外
        var result = await sut.ExecuteAsync(new CheckInRequest
        {
            EventId = TestEventId, Account = TestAccount,
            Nonce = nonce, Sig = ComputeSig(nonce, gracePeriodToken.ExpiresAt)
        });

        // Assert
        result.CheckedInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        await uow.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCheckedIn_ThrowsC4004()
    {
        // Arrange
        var (sut, repo, _, _) = BuildSut();
        var nonce     = new string('f', 64);
        var token     = ValidToken(nonce);
        var responder = ValidResponder();
        responder.CheckInStatus = true;   // 已報到

        repo.FindQrTokenByNonceAsync(nonce, default).Returns(token);
        repo.FindEventByIdAsync(TestEventId, default).Returns(ActiveEvent());
        repo.FindResponderByEventAndAccountAsync(TestEventId, TestAccount, default)
            .Returns(responder);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CheckInException>(
            () => sut.ExecuteAsync(new CheckInRequest
            {
                EventId = TestEventId, Account = TestAccount,
                Nonce = nonce, Sig = ComputeSig(nonce, token.ExpiresAt)
            }));
        ex.ErrorCode.Should().Be("C4004");
    }

    [Fact]
    public async Task ExecuteAsync_AccountNotInList_ThrowsA7001()
    {
        // Arrange
        var (sut, repo, _, _) = BuildSut();
        var nonce = new string('g', 64);
        var token = ValidToken(nonce);

        repo.FindQrTokenByNonceAsync(nonce, default).Returns(token);
        repo.FindEventByIdAsync(TestEventId, default).Returns(ActiveEvent());
        repo.FindResponderByEventAndAccountAsync(TestEventId, TestAccount, default)
            .Returns((EventResponder?)null);   // 不在名單

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CheckInException>(
            () => sut.ExecuteAsync(new CheckInRequest
            {
                EventId = TestEventId, Account = TestAccount,
                Nonce = nonce, Sig = ComputeSig(nonce, token.ExpiresAt)
            }));
        ex.ErrorCode.Should().Be("A7001");
    }
}
