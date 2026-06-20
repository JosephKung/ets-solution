// src/Ets.Infrastructure/Outbox/Handlers/InviteChatMemberOutboxHandler.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Outbox.Handlers;

/// <summary>
/// InviteChatMember Outbox Handler（WBS 1.3.6）
///
/// 消費流程（§6.5.2）：
///   1. 反序列化 Payload
///   2. 冪等性：EventResponder.JoinedChatRoom 已為 true → 跳過
///   3. 呼叫 ITeamPlusSystemClient.InviteChatMemberAsync
///   4. 成功 → UPDATE EventResponders.JoinedChatRoom = true
///   5. IgnoredMemberList 非空 → 寫 AuditLog
/// </summary>
public sealed class InviteChatMemberOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly AppDbContext _db;
    private readonly ILogger<InviteChatMemberOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.InviteChatMember;

    public InviteChatMemberOutboxHandler(
        ITeamPlusSystemClient systemClient,
        AppDbContext db,
        ILogger<InviteChatMemberOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<InviteMemberOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（InviteChatMember）");

        _logger.LogInformation(
            "InviteChatMember 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "Member={Member}, ChatSn={ChatSn}",
            outboxId, payload.EventId, payload.MemberAccount, payload.TargetSn);

        // ── 冪等性：JoinedChatRoom 已為 true → 跳過 ──────────────
        var responder = await _db.EventResponders
            .FirstOrDefaultAsync(r =>
                r.EventId == payload.EventId &&
                r.Account == payload.MemberAccount, ct)
            ?? throw new InvalidOperationException(
                $"EventResponder 找不到 EventId={payload.EventId}, Account={payload.MemberAccount}");

        if (responder.JoinedChatRoom)
        {
            _logger.LogWarning(
                "InviteChatMember 冪等跳過：EventId={EventId}, Member={Member} 已在 ChatRoom",
                payload.EventId, payload.MemberAccount);
            return;
        }

        // ── 呼叫 team+ inviteChatMember ───────────────────────────
        var request = new InviteChatMemberRequest(
            ChatSN:          payload.TargetSn,
            OperatorAccount: payload.OperatorAccount,
            MemberList:      [payload.MemberAccount]);

        var result = await _systemClient.InviteChatMemberAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"inviteChatMember 失敗：EventId={payload.EventId}, " +
                $"Member={payload.MemberAccount}, ErrorCode={result.ErrorCode}");
        }

        // ── 回填 JoinedChatRoom = true ────────────────────────────
        responder.JoinedChatRoom = true;
        responder.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "InviteChatMember 完成：EventId={EventId}, Member={Member}, ChatSn={ChatSn}",
            payload.EventId, payload.MemberAccount, payload.TargetSn);
    }
}
