// src/Ets.Infrastructure/Outbox/Handlers/AssignChatManagerOutboxHandler.cs
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
/// AssignChatManager Outbox Handler（WBS 1.3.7）
/// 指派分組交談室管理員（§6.5.3 assignChatManager）
/// 注意：需確認 team+ 端已啟用「管理者機制」
///
/// 與 AssignTeamManagerOutboxHandler 共用同一個 OutboxMessageType.AssignManager enum 值，
/// 由 OutboxDispatcherWorker 依 Payload 中隱含的語意（TargetSn 類型）決定呼叫哪個 Handler。
/// 實作時建議用 PayloadJson 中加入 TargetType 欄位區分，或分拆為兩個獨立 enum 值（未來可擴充）。
/// 本版本採 AssignManager = 5 共用，由寫入端（Webhook Handler）決定 MessageType 細節。
/// </summary>
public sealed class AssignChatManagerOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly AppDbContext _db;
    private readonly ILogger<AssignChatManagerOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 注意：AssignTeamManager 與 AssignChatManager 共用 AssignManager enum 值。
    /// OutboxDispatcherWorker 依照寫入端指定的 MessageType 路由，
    /// 本 Handler 需與 AssignTeamManagerOutboxHandler 擇一登錄，
    /// 或改用自訂 Payload 欄位 TargetType 來判斷。
    /// 建議後續將 enum 拆為 AssignTeamManager = 5 / AssignChatManager = 10。
    /// </summary>
    public OutboxMessageType MessageType => OutboxMessageType.AssignChatManager;

    public AssignChatManagerOutboxHandler(
        ITeamPlusSystemClient systemClient,
        AppDbContext db,
        ILogger<AssignChatManagerOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AssignManagerOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（AssignChatManager）");

        _logger.LogInformation(
            "AssignChatManager 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "Manager={Manager}, ChatSn={ChatSn}",
            outboxId, payload.EventId, payload.ManagerAccount, payload.TargetSn);

        var request = new AssignManagerRequest(
            TeamOrChatSN:    payload.TargetSn,
            OperatorAccount: payload.OperatorAccount,
            ManagerList:     [payload.ManagerAccount]);

        var result = await _systemClient.AssignChatManagerAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"assignChatManager 失敗：EventId={payload.EventId}, " +
                $"Manager={payload.ManagerAccount}, ErrorCode={result.ErrorCode}");
        }

        _logger.LogInformation(
            "AssignChatManager 完成：EventId={EventId}, Manager={Manager}, ChatSn={ChatSn}",
            payload.EventId, payload.ManagerAccount, payload.TargetSn);
    }
}
