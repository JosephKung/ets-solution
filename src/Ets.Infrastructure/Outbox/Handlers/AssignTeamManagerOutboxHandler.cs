// src/Ets.Infrastructure/Outbox/Handlers/AssignTeamManagerOutboxHandler.cs
using System.Text.Json;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Outbox.Handlers;

/// <summary>
/// AssignTeamManager Outbox Handler（WBS 1.3.7）
/// 指派大團隊管理員（§6.5.3 assignTeamManager）
/// 用於事件中期動態提升指揮官以外之人員為 Team Manager
/// </summary>
public sealed class AssignTeamManagerOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly AppDbContext _db;
    private readonly ILogger<AssignTeamManagerOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.AssignTeamManager;

    public AssignTeamManagerOutboxHandler(
        ITeamPlusSystemClient systemClient,
        AppDbContext db,
        ILogger<AssignTeamManagerOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AssignManagerOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（AssignTeamManager）");

        _logger.LogInformation(
            "AssignTeamManager 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "Manager={Manager}, TeamSn={TeamSn}",
            outboxId, payload.EventId, payload.ManagerAccount, payload.TargetSn);

        var request = new AssignManagerRequest(
            TeamOrChatSN:    payload.TargetSn,
            OperatorAccount: payload.OperatorAccount,
            ManagerList:     [payload.ManagerAccount]);

        var result = await _systemClient.AssignTeamManagerAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"assignTeamManager 失敗：EventId={payload.EventId}, " +
                $"Manager={payload.ManagerAccount}, ErrorCode={result.ErrorCode}");
        }

        _logger.LogInformation(
            "AssignTeamManager 完成：EventId={EventId}, Manager={Manager}, TeamSn={TeamSn}",
            payload.EventId, payload.ManagerAccount, payload.TargetSn);
    }
}
