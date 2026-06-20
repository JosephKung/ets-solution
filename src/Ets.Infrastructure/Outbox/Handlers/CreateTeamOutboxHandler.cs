// src/Ets.Infrastructure/Outbox/Handlers/CreateTeamOutboxHandler.cs
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
/// CreateTeam Outbox Handler（WBS 1.3.4）
///
/// 消費流程（§6.2 ETS 處理流程）：
///   1. 反序列化 Payload → 呼叫 ITeamPlusSystemClient.CreateTeamAsync
///   2. 成功 → UPDATE EmergencyEvents.TeamPlusBigTeamSn
///   3. IgnoredMemberList 非空 → 寫 AuditLog
///   4. IgnoredManagerList 非空 → 寫 AuditLog（嚴重警告）
///   5. 插入後續 Outbox：CreateTeamAPIAccount（§6.6）
/// </summary>
public sealed class CreateTeamOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly AppDbContext _db;
    private readonly ILogger<CreateTeamOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.CreateTeam;

    public CreateTeamOutboxHandler(
        ITeamPlusSystemClient systemClient,
        AppDbContext db,
        ILogger<CreateTeamOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        // ── 1. 反序列化 Payload ────────────────────────────────────
        var payload = JsonSerializer.Deserialize<CreateTeamOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxID={outboxId} Payload 反序列化失敗（CreateTeam）");

        _logger.LogInformation(
            "CreateTeam 開始：OutboxID={OutboxId}, EventId={EventId}",
            outboxId, payload.EventId);

        // ── 冪等性防重：若已有 TeamSN，跳過（§11.3）────────────────
        var ev = await _db.EmergencyEvents
            .FirstOrDefaultAsync(e => e.EventId == payload.EventId, ct)
            ?? throw new InvalidOperationException(
                $"EmergencyEvents 找不到 EventId={payload.EventId}");

        if (ev.TeamPlusBigTeamSn.HasValue)
        {
            _logger.LogWarning(
                "CreateTeam 冪等跳過：EventId={EventId} TeamSn 已存在={TeamSn}",
                payload.EventId, ev.TeamPlusBigTeamSn);
            return;
        }

        // ── 2. 呼叫 team+ createTeam ──────────────────────────────
        var request = new CreateTeamRequest(
            Owner:       payload.CommanderAccounts[0],
            Name:        payload.TeamName,
            Subject:     payload.Subject,
            Description: payload.Description,
            MemberList:  payload.MemberAccounts,
            ManagerList: payload.ManagerAccounts);

        var result = await _systemClient.CreateTeamAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"createTeam 失敗：EventId={payload.EventId}, " +
                $"ErrorCode={result.ErrorCode}, Description={result.Description}");
        }

        _logger.LogInformation(
            "createTeam 成功：EventId={EventId}, TeamSn={TeamSn}",
            payload.EventId, result.TeamSN);

        // ── 3. 回填 EmergencyEvents.TeamPlusBigTeamSn ─────────────
        ev.TeamPlusBigTeamSn = (int)result.TeamSN;

        // ── 4. 處理 IgnoredMemberList ─────────────────────────────
        if (result.IgnoredMemberList.Count > 0)
        {
            _logger.LogWarning(
                "createTeam IgnoredMemberList 非空：EventId={EventId}, Ignored=[{Accounts}]",
                payload.EventId, string.Join(",", result.IgnoredMemberList));

            _db.AuditLogs.Add(new AuditLog
            {
                Category  = "STATUS_CHANGE",
                EventId   = payload.EventId,
                Actor     = "OutboxDispatcher",
                Action    = "CreateTeam_IgnoredMember",
                Detail    = $"IgnoredMemberList: {string.Join(",", result.IgnoredMemberList)}",
                CreatedAt = DateTime.UtcNow
            });
        }

        // ── 5. 處理 IgnoredManagerList（嚴重警告）────────────────
        if (result.IgnoredManagerList.Count > 0)
        {
            _logger.LogError(
                "createTeam IgnoredManagerList 非空（嚴重）：EventId={EventId}, Ignored=[{Accounts}]",
                payload.EventId, string.Join(",", result.IgnoredManagerList));

            _db.AuditLogs.Add(new AuditLog
            {
                Category  = "STATUS_CHANGE",
                EventId   = payload.EventId,
                Actor     = "OutboxDispatcher",
                Action    = "CreateTeam_IgnoredManager_CRITICAL",
                Detail    = $"[CRITICAL] IgnoredManagerList: " +
                            $"{string.Join(",", result.IgnoredManagerList)}",
                CreatedAt = DateTime.UtcNow
            });
        }

        // ── 6. 插入後續 Outbox：CreateTeamAPIAccount（§6.6，1.3.10 實作）──
        var apiAccountPayload = JsonSerializer.Serialize(new
        {
            EventId      = payload.EventId,
            TeamSn       = result.TeamSN,
            OwnerAccount = payload.CommanderAccounts[0]
        });

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventId     = payload.EventId,
            MessageType = OutboxMessageType.CreateTeamAPIAccount,
            PayloadJson = apiAccountPayload,
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CreateTeam 完成：EventId={EventId}, TeamSn={TeamSn}, 已排入 CreateTeamAPIAccount Outbox",
            payload.EventId, result.TeamSN);
    }
}
