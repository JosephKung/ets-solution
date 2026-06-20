// src/Ets.Infrastructure/Outbox/Handlers/CreateChatOutboxHandler.cs
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
/// CreateChat Outbox Handler（WBS 1.3.5）
///
/// 消費流程（§6.3 ETS 處理流程）：
///   1. 反序列化 Payload
///   2. 分流邏輯：若 SplitGroupIndex 有值，交談室名稱加上 "-N" 後綴（如 "(A0021)消防組-1"）
///   3. 呼叫 ITeamPlusSystemClient.CreateChatAsync
///   4. 成功 → UPDATE EventGroups.TeamPlusChatSn + CreatorAccount
///   5. IgnoredMemberList / IgnoredManagerList 非空 → 寫 AuditLog
///
/// 200 人分流設計（§6.3）：
///   - 單組 ≤ 200 人：SplitGroupIndex = null，標準流程，1 chatGP = 1 chat room
///   - 單組 > 200 人（v2.4 假設不會發生，待業主確認）：
///     M2 EventTriggerHandler 寫入多筆 CreateChat Outbox，SplitGroupIndex = 1,2,3...
///     本 Handler 依 SplitGroupIndex 產生名稱後綴並分別建立
/// </summary>
public sealed class CreateChatOutboxHandler : IOutboxHandler
{
    private readonly ITeamPlusSystemClient _systemClient;
    private readonly AppDbContext _db;
    private readonly ILogger<CreateChatOutboxHandler> _logger;

    /// <summary>交談室名稱 team+ 上限 20 字（§6.3）</summary>
    private const int ChatNameMaxLength = 20;

    /// <summary>分流後綴長度（如 "-1" = 2 字），保留給後綴用</summary>
    private const int SplitSuffixMaxLength = 3;  // "-99" 最長

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxMessageType MessageType => OutboxMessageType.CreateChat;

    public CreateChatOutboxHandler(
        ITeamPlusSystemClient systemClient,
        AppDbContext db,
        ILogger<CreateChatOutboxHandler> logger)
    {
        _systemClient = systemClient;
        _db           = db;
        _logger       = logger;
    }

    public async Task HandleAsync(long outboxId, string payloadJson, CancellationToken ct)
    {
        // ── 1. 反序列化 Payload ────────────────────────────────────
        var payload = JsonSerializer.Deserialize<CreateChatOutboxPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"OutboxId={outboxId} Payload 反序列化失敗（CreateChat）");

        _logger.LogInformation(
            "CreateChat 開始：OutboxId={OutboxId}, EventId={EventId}, " +
            "GroupId={GroupId}, ChatGp={ChatGp}, SplitGroupIndex={SplitGroupIndex}",
            outboxId, payload.EventId, payload.GroupId,
            payload.ChatGp, payload.SplitGroupIndex);

        // ── 冪等性防重：若 EventGroup 已有 ChatSn，跳過（§11.3）──
        var group = await _db.EventGroups
            .FirstOrDefaultAsync(g => g.GroupId == payload.GroupId, ct)
            ?? throw new InvalidOperationException(
                $"EventGroups 找不到 GroupId={payload.GroupId}");

        if (group.TeamPlusChatSn.HasValue)
        {
            _logger.LogWarning(
                "CreateChat 冪等跳過：GroupId={GroupId} ChatSn 已存在={ChatSn}",
                payload.GroupId, group.TeamPlusChatSn);
            return;
        }

        // ── 2. 200 人分流：組裝交談室名稱 ─────────────────────────
        // 標準流程（SplitGroupIndex = null）：使用原始 ChatGp 名稱（截斷至 20 字）
        // 分流流程（SplitGroupIndex = 1,2,3...）：名稱加後綴 "-N"，整體截斷至 20 字
        var chatName = BuildChatName(payload.ChatGp, payload.SplitGroupIndex);

        _logger.LogDebug(
            "CreateChat 交談室名稱：原始={Original}, 最終={Final}",
            payload.ChatGp, chatName);

        // ── 3. 呼叫 team+ createChat ──────────────────────────────
        var request = new CreateChatRequest(
            CreatorAccount: payload.CreatorAccount,
            ChatName:       chatName,
            MemberList:     payload.MemberAccounts,
            ManagerList:    payload.ManagerAccounts);

        var result = await _systemClient.CreateChatAsync(request, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"createChat 失敗：GroupId={payload.GroupId}, ChatGp={payload.ChatGp}, " +
                $"ErrorCode={result.ErrorCode}, Description={result.Description}");
        }

        _logger.LogInformation(
            "createChat 成功：GroupId={GroupId}, ChatGp={ChatGp}, ChatSn={ChatSn}",
            payload.GroupId, payload.ChatGp, result.ChatSN);

        // ── 4. 回填 EventGroups.TeamPlusChatSn + CreatorAccount ───
        group.TeamPlusChatSn  = (int)result.ChatSN;
        group.CreatorAccount  = payload.CreatorAccount;
        group.MemberCount     = payload.MemberAccounts.Count;

        // ── 5. 處理 IgnoredMemberList ─────────────────────────────
        if (result.IgnoredMemberList.Count > 0)
        {
            _logger.LogWarning(
                "createChat IgnoredMemberList 非空：GroupId={GroupId}, Ignored=[{Accounts}]",
                payload.GroupId, string.Join(",", result.IgnoredMemberList));

            _db.AuditLogs.Add(new AuditLog
            {
                Category  = "STATUS_CHANGE",
                EventId   = payload.EventId,
                Actor     = "OutboxDispatcher",
                Action    = "CreateChat_IgnoredMember",
                Detail    = $"GroupId={payload.GroupId}, ChatGp={payload.ChatGp}, " +
                            $"IgnoredMemberList: {string.Join(",", result.IgnoredMemberList)}",
                CreatedAt = DateTime.UtcNow
            });
        }

        // ── 6. 處理 IgnoredManagerList（嚴重警告）────────────────
        if (result.IgnoredManagerList.Count > 0)
        {
            _logger.LogError(
                "createChat IgnoredManagerList 非空（嚴重）：GroupId={GroupId}, Ignored=[{Accounts}]",
                payload.GroupId, string.Join(",", result.IgnoredManagerList));

            _db.AuditLogs.Add(new AuditLog
            {
                Category  = "STATUS_CHANGE",
                EventId   = payload.EventId,
                Actor     = "OutboxDispatcher",
                Action    = "CreateChat_IgnoredManager_CRITICAL",
                Detail    = $"[CRITICAL] GroupId={payload.GroupId}, ChatGp={payload.ChatGp}, " +
                            $"IgnoredManagerList: {string.Join(",", result.IgnoredManagerList)}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CreateChat 完成：GroupId={GroupId}, ChatGp={ChatGp}, ChatSn={ChatSn}",
            payload.GroupId, payload.ChatGp, result.ChatSN);
    }

    // ═══════════════════════════════════════════════════════════════
    // 私有輔助方法
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 組裝交談室名稱，考量 team+ 20 字限制及分流後綴
    ///
    /// 規則：
    ///   - 無分流（SplitGroupIndex = null）：直接截斷至 20 字
    ///   - 有分流（SplitGroupIndex = 1,2...）：
    ///     先截斷原始名稱至 (20 - 後綴長度) 字，再加後綴 "-N"
    ///     例如：ChatGp="(A0021)消防組", SplitGroupIndex=1
    ///           後綴 = "-1"（2字），原始截斷至 18 字
    ///           最終 = "(A0021)消防組-1"（在 20 字內）
    /// </summary>
    public static string BuildChatName(string chatGp, int? splitGroupIndex)
    {
        if (splitGroupIndex is null)
        {
            // 標準流程：直接截斷
            return chatGp.Length <= ChatNameMaxLength
                ? chatGp
                : chatGp[..ChatNameMaxLength];
        }

        // 分流流程：後綴 = "-{N}"
        var suffix     = $"-{splitGroupIndex}";
        var maxBaseLen = ChatNameMaxLength - suffix.Length;

        var baseName = chatGp.Length <= maxBaseLen
            ? chatGp
            : chatGp[..maxBaseLen];

        return baseName + suffix;
    }
}
