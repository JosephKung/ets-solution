// src/Ets.Infrastructure/Outbox/Handlers/InviteTeamMemberOutboxHandler.cs
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
/// InviteTeamMember Outbox Handler（WBS 1.3.6）
///
/// 消費流程（§6.5.1）：
///   1. 反序列化 Payload
///   2. 冪等性：EventResponder.JoinedTeam 已為 true → 跳過
///   3. 呼叫 ITeamPlusSystemClient.InviteTeamMemberAsync
///   4. 成功 → UPDATE EventResponders.JoinedTeam = true
///   5. IgnoredMemberList 非空 → 寫 AuditLog
/// </summary>
public sealed class InviteTeamMemberOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly AppDbContext _db;
    private readonly ILogger<InviteTeamMemberOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.InviteTeamMember;

    public InviteTeamMemberOutboxHandler(
        ITeamPlusSystemClient systemClient,
        AppDbContext db,
        ILogger<InviteTeamMemberOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<InviteMemberOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（InviteTeamMember）");

        _logger.LogInformation(
            "InviteTeamMember 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "Member={Member}, TeamSn={TeamSn}",
            outboxId, payload.EventId, payload.MemberAccount, payload.TargetSn);

        // ── 冪等性：JoinedTeam 已為 true → 跳過 ─────────────────
        var responder = await _db.EventResponders
            .FirstOrDefaultAsync(r =>
                r.EventId == payload.EventId &&
                r.Account == payload.MemberAccount, ct)
            ?? throw new InvalidOperationException(
                $"EventResponder 找不到 EventId={payload.EventId}, Account={payload.MemberAccount}");

        if (responder.JoinedTeam)
        {
            _logger.LogWarning(
                "InviteTeamMember 冪等跳過：EventId={EventId}, Member={Member} 已在 Team",
                payload.EventId, payload.MemberAccount);
            return;
        }

        // ── 呼叫 team+ inviteTeamMember ───────────────────────────
        var request = new InviteTeamMemberRequest(
            TeamSN:          payload.TargetSn,
            OperatorAccount: payload.OperatorAccount,
            MemberList:      [payload.MemberAccount]);

        var result = await _systemClient.InviteTeamMemberAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"inviteTeamMember 失敗：EventId={payload.EventId}, " +
                $"Member={payload.MemberAccount}, ErrorCode={result.ErrorCode}");
        }

        // ── 回填 JoinedTeam = true ────────────────────────────────
        responder.JoinedTeam = true;
        responder.UpdatedAt  = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "InviteTeamMember 完成：EventId={EventId}, Member={Member}, TeamSn={TeamSn}",
            payload.EventId, payload.MemberAccount, payload.TargetSn);
    }
}
