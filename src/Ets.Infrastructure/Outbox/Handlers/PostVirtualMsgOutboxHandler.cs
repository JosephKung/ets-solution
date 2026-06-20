// src/Ets.Infrastructure/Outbox/Handlers/PostVirtualMsgOutboxHandler.cs
using System.Text.Json;
using Ets.Application.Interfaces;
using Ets.Application.Interfaces.External;
using Ets.Application.UseCases.TeamPlus;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Outbox.Handlers;

/// <summary>
/// PostVirtualMsg Outbox Handler（WBS 1.3.10 第二段）
///
/// 消費流程（§6.6.4 postMessage）：
///   1. 反序列化 Payload
///   2. 冪等性：TeamPlusArticleBatchId 已有值 → 跳過
///   3. 從 DB 讀取加密 ApiKey → 解密（1.3.11）
///   4. 呼叫 ITeamPlusSystemClient.PostTeamMessageAsync
///   5. 成功 → 回填 EmergencyEvents.TeamPlusArticleBatchId
/// </summary>
public sealed class PostVirtualMsgOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly IVirtualAccountKeyEncryptor _encryptor;
    private readonly AppDbContext _db;
    private readonly ILogger<PostVirtualMsgOutboxHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.PostVirtualMsg;

    public PostVirtualMsgOutboxHandler(
        ITeamPlusSystemClient systemClient,
        IVirtualAccountKeyEncryptor encryptor,
        AppDbContext db,
        ILogger<PostVirtualMsgOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _encryptor    = encryptor;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PostVirtualMsgOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（PostVirtualMsg）");

        _logger.LogInformation(
            "PostVirtualMsg 開始：OutboxId={OutboxId}, EventId={EventId}",
            outboxId, payload.EventId);

        // ── 冪等性：BatchId 已有值 → 跳過 ────────────────────────
        var ev = await _db.EmergencyEvents
            .FirstOrDefaultAsync(e => e.EventId == payload.EventId, ct)
            ?? throw new InvalidOperationException(
                $"EmergencyEvents 找不到 EventId={payload.EventId}");

        if (!string.IsNullOrEmpty(ev.TeamPlusArticleBatchId))
        {
            _logger.LogWarning(
                "PostVirtualMsg 冪等跳過：EventId={EventId} BatchId 已存在",
                payload.EventId);
            return;
        }

        // ── 從 DB 取得加密 ApiKey 並解密（1.3.11）────────────────
        if (ev.TeamPlusVirtualAccountApiKey is null || ev.TeamPlusVirtualAccountApiKey.Length == 0)
            throw new InvalidOperationException(
                $"EmergencyEvents.TeamPlusVirtualAccountApiKey 為空：EventId={payload.EventId}，" +
                "請確認 CreateTeamAPIAccount 已完成");

        if (string.IsNullOrEmpty(ev.TeamPlusVirtualAccount))
            throw new InvalidOperationException(
                $"EmergencyEvents.TeamPlusVirtualAccount 為空：EventId={payload.EventId}");

        var virtualApiKey = _encryptor.Decrypt(ev.TeamPlusVirtualAccountApiKey);

        // ── 呼叫 team+ postMessage ────────────────────────────────
        var result = await _systemClient.PostTeamMessageAsync(
            virtualAccount: ev.TeamPlusVirtualAccount,
            virtualApiKey:  virtualApiKey,
            teamSn:         payload.TeamSn,
            textContent:    payload.TextContent,
            subject:        payload.Subject,
            ct:             ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"postMessage 失敗：EventId={payload.EventId}, " +
                $"ErrorCode={result.ErrorCode}, Description={result.Description}");
        }

        // ── 回填 TeamPlusArticleBatchId ───────────────────────────
        ev.TeamPlusArticleBatchId = result.BatchId;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PostVirtualMsg 完成：EventId={EventId}, BatchId={BatchId}",
            payload.EventId, result.BatchId);
    }
}
