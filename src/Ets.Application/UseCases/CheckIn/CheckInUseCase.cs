using Ets.Application.Abstractions;
using Ets.Application.Dtos.CheckIn;
using Ets.Application.Interfaces;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ets.Application.UseCases.CheckIn;

/// <summary>
/// 現場報到 Use Case（§8.5）。
/// 驗證 QR Token → 標記報到 → 補位 Outbox → 觸發 SignalR 推播。
/// </summary>
public class CheckInUseCase
{
    private readonly IEtsRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDashboardNotifier _dashboardNotifier;
    private readonly ILogger<CheckInUseCase> _logger;
    private readonly string _hmacKey;

    public CheckInUseCase(
        IEtsRepository repository,
        IUnitOfWork unitOfWork,
        IDashboardNotifier dashboardNotifier,
        ILogger<CheckInUseCase> logger,
        IQrHmacKeyProvider hmacKeyProvider)
    {
        _repository        = repository;
        _unitOfWork        = unitOfWork;
        _dashboardNotifier = dashboardNotifier;
        _logger            = logger;
        _hmacKey           = hmacKeyProvider.GetKey();
    }

    /// <summary>
    /// 執行報到流程。
    /// </summary>
    /// <returns>報到結果</returns>
    /// <exception cref="CheckInException">報到驗證失敗時拋出，含錯誤碼</exception>
    public async Task<CheckInResult> ExecuteAsync(
        CheckInRequest request,
        CancellationToken ct = default)
    {
        // ── Step 1：驗證 Nonce 存在且對應 event_id ───────────────────────
        var token = await _repository.FindQrTokenByNonceAsync(request.Nonce, ct);

        if (token is null || token.EventId != request.EventId)
        {
            _logger.LogWarning("報到失敗：Nonce 不存在或 EventId 不符。EventId={EventId} Nonce={Nonce}",
                request.EventId, request.Nonce);
            throw new CheckInException("C4001", "QR Token 已過期或無效，請重新掃描");
        }

        // ── Step 2：驗證 HMAC 簽章 ───────────────────────────────────────
        if (!VerifySignature(request.Nonce, token.ExpiresAt, request.Sig))
        {
            _logger.LogWarning("報到失敗：HMAC 簽章無效。Nonce={Nonce}", request.Nonce);
            throw new CheckInException("C4002", "QR Token 簽章無效");
        }

        // ── Step 3：驗證有效性（含 Grace Period 邏輯，§8.5）────────────
        var now = DateTime.UtcNow;
        var isValid = (token.IsActive && now < token.ExpiresAt)
                   || (!token.IsActive && token.GracePeriodEndAt.HasValue && now < token.GracePeriodEndAt);

        if (!isValid)
        {
            _logger.LogWarning("報到失敗：Token 已過期且超過寬限期。Nonce={Nonce}", request.Nonce);
            throw new CheckInException("C4001", "QR Token 已過期且超過寬限期，請重新掃描");
        }

        // ── Step 4：驗證事件存在且進行中 ────────────────────────────────
        var ev = await _repository.FindEventByIdAsync(request.EventId, ct);

        if (ev is null || ev.Status != 0)
        {
            _logger.LogWarning("報到失敗：事件不存在或已結案。EventId={EventId}", request.EventId);
            throw new CheckInException("C4005", "事件不存在或已結案");
        }

        // ── Step 5：驗證 Account 是否在應變名單中 ───────────────────────
        var responder = await _repository.FindResponderByEventAndAccountAsync(
            request.EventId, request.Account, ct);

        if (responder is null)
        {
            // 非名單成員 → 轉臨時支援流程（§8.5 Step 4b，1.5.8 實作）
            _logger.LogInformation("報到帳號不在名單，轉臨時申請。EventId={EventId} Account={Account}",
                request.EventId, request.Account);
            throw new CheckInException("A7001", "帳號不在應變名單，已轉為臨時支援申請");
        }

        // ── Step 6：防重複報到 ────────────────────────────────────────────
        if (responder.CheckInStatus)
        {
            _logger.LogWarning("報到失敗：已重複報到。EventId={EventId} Account={Account}",
                request.EventId, request.Account);
            throw new CheckInException("C4004", "該帳號已完成報到");
        }

        var checkInAt = DateTime.UtcNow;
        responder.CheckInStatus = true;
        responder.CheckInAt     = checkInAt;
        responder.UpdatedAt     = checkInAt;

        // ── Step 7：補位（防漏網）— 若尚未加入團隊或交談室 ─────────────
        var addedToTeam     = false;
        var addedToChatRoom = false;
        string? chatRoomName = null;

        if (!responder.JoinedTeam)
        {
            await _repository.AddOutboxMessageAsync(new OutboxMessage
            {
                EventId     = request.EventId,
                MessageType = OutboxMessageType.InviteTeamMember,
                PayloadJson = JsonSerializer.Serialize(
                    new { EventId = request.EventId, Account = request.Account }),
                Status    = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow
            }, ct);
            addedToTeam = true;
        }

        if (!responder.JoinedChatRoom)
        {
            await _repository.AddOutboxMessageAsync(new OutboxMessage
            {
                EventId     = request.EventId,
                MessageType = OutboxMessageType.InviteChatMember,
                PayloadJson = JsonSerializer.Serialize(
                    new { EventId = request.EventId, Account = request.Account }),
                Status    = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow
            }, ct);
            addedToChatRoom = true;
            chatRoomName    = responder.ChatGp;
        }

        // ── Step 8：寫入 AuditLog ────────────────────────────────────────
        await _repository.AddAuditLogAsync(new AuditLog
        {
            Category  = "STATUS_CHANGE",
            EventId   = request.EventId,
            Actor     = request.Account,
            Action    = "CheckIn",
            Detail    = $"報到成功。JoinedTeam={responder.JoinedTeam}, JoinedChatRoom={responder.JoinedChatRoom}",
            CreatedAt = checkInAt
        }, ct);

        // ── Step 9：一次性 Commit（EventResponder + Outbox + AuditLog）──
        await _unitOfWork.SaveChangesAsync(ct);

        // ── Step 10：觸發 SignalR 推播（DomainEvent: CheckInRegistered）─
        await _dashboardNotifier.NotifyCheckInAsync(request.EventId, request.Account, checkInAt, ct);

        _logger.LogInformation("報到成功。EventId={EventId} Account={Account}",
            request.EventId, request.Account);

        return new CheckInResult
        {
            CheckedInAt     = checkInAt,
            AddedToTeam     = addedToTeam,
            AddedToChatRoom = addedToChatRoom,
            ChatRoomName    = chatRoomName
        };
    }

    /// <summary>
    /// 驗證 HMAC-SHA256 短簽章（取首 8 字元）。
    /// 簽章內容 = HMAC(nonce + "|" + exp_unix_timestamp)
    /// </summary>
    private bool VerifySignature(string nonce, DateTime expiresAt, string sig)
    {
        var expUnix = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var message = $"{nonce}|{expUnix}";

        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(_hmacKey));
        var hash     = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(message));
        var hashHex  = Convert.ToHexString(hash).ToLowerInvariant();
        var expected = hashHex[..8];

        // 使用固定時間比較，防 Timing Attack
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(sig.ToLowerInvariant()));
    }
}
