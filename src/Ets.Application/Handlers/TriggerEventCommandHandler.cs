using Ets.Application.Abstractions;
using Ets.Application.Commands;
using Ets.Application.Dtos;
using Ets.Application.Exceptions;
using Ets.Application.Services;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ets.Application.Handlers;

/// <summary>
/// HIS 事件觸發 Command Handler。
/// 依規格書 §5.2 後端處理流程（step 3.5~8）：
///   1.2.4 — 事件防重（EventID PK 檢查）
///   1.2.3 — InferIntent（FlexMsgIntentMap 產生）
///   1.2.5 — DB Transaction：INSERT EmergencyEvents + Groups + Responders + OutboxMessages
/// </summary>
public sealed class TriggerEventCommandHandler : IRequestHandler<TriggerEventCommand, TriggerEventResult>
{
    private readonly IEtsRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TriggerEventCommandHandler> _logger;

    public TriggerEventCommandHandler(
        IEtsRepository repo,
        IUnitOfWork uow,
        ILogger<TriggerEventCommandHandler> logger)
    {
        _repo   = repo;
        _uow    = uow;
        _logger = logger;
    }

    public async Task<TriggerEventResult> Handle(
        TriggerEventCommand request,
        CancellationToken cancellationToken)
    {
        var dto = request.Dto;

        // ── 步驟 4：事件防重（1.2.4）──────────────────────────────────────
        var exists = await _repo.EventExistsAsync(dto.EventId, cancellationToken);
        if (exists)
        {
            _logger.LogWarning("事件防重：EventId={EventId} 已存在，回傳 409", dto.EventId);
            return TriggerEventResult.Conflict(dto.EventId);
        }

        // ── 步驟 5：推導 IntentMap（1.2.3）───────────────────────────────
        var intentMap     = IntentInferenceService.BuildIntentMap(dto.EventFlexMsgItems);
        var intentMapJson = IntentInferenceService.SerializeIntentMap(intentMap);

        _logger.LogDebug(
            "IntentMap 產生完成：EventId={EventId}, IntentCount={Count}",
            dto.EventId, intentMap.Count);

        // ── 步驟 6：BEGIN TRANSACTION（1.2.5）────────────────────────────
        await using var tran = await _uow.BeginTransactionAsync(cancellationToken);

        try
        {
            var now       = DateTime.UtcNow;
            var eventTime = DateTime.ParseExact(
                dto.EventTime,
                "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture);

            // ── 6a：INSERT EmergencyEvents ─────────────────────────────────
            var ev = new EmergencyEvent
            {
                EventId              = dto.EventId,
                EventType            = dto.EventType.ToLowerInvariant(),
                EventTime            = eventTime,
                EventSummary         = dto.EventSummary,
                EventDescription     = dto.EventDescription,
                EventArea            = dto.EventArea,
                AudioContent         = dto.AudioContent,
                EventSource          = dto.EventSource,
                FlexMsgItemsJson     = dto.EventFlexMsgItems,
                FlexMsgIntentMapJson = intentMapJson,
                Status               = EventStatus.Processing,
                CreatedAt            = now,
                UpdatedAt            = now
            };
            await _repo.AddEventAsync(ev, cancellationToken);

            // ── 6b：INSERT EventGroups ────────────────────────────────────
            foreach (var groupDto in dto.EventGroups)
            {
                var group = new EventGroup
                {
                    EventId     = dto.EventId,
                    ChatGp      = groupDto.ChatGp.Length > 20
                                    ? groupDto.ChatGp[..20]
                                    : groupDto.ChatGp,
                    Description = groupDto.Description,
                    CreatedAt   = now
                };
                await _repo.AddGroupAsync(group, cancellationToken);
            }

            // ── 6c：INSERT EventResponders ────────────────────────────────
            var commanderAccounts = ParseJsonStringArray(dto.EventCommander);

            foreach (var responderDto in dto.EventResponders)
            {
                var effectiveRole = commanderAccounts.Contains(responderDto.Account)
                    ? "commander"
                    : responderDto.Role;

                var (displayName, phoneNumber) = ParseDescription(responderDto.Description);

                var responder = new EventResponder
                {
                    EventId      = dto.EventId,
                    Account      = responderDto.Account,
                    DisplayName  = displayName,
                    PhoneNumber  = phoneNumber,
                    Description  = responderDto.Description,
                    Role         = effectiveRole,
                    ChatGp       = responderDto.ChatGp,
                    ReplyStatus  = "Pending",
                    ReplyChannel = "None",
                    CreatedAt    = now,
                    UpdatedAt    = now
                };
                await _repo.AddResponderAsync(responder, cancellationToken);
            }

            // ── 6d：INSERT OutboxMessages ─────────────────────────────────
            var outboxMessages = BuildOutboxMessages(dto, now);
            foreach (var msg in outboxMessages)
                await _repo.AddOutboxMessageAsync(msg, cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);
            await _uow.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "事件觸發成功：EventId={EventId}, Groups={G}, Responders={R}, Outbox={O}",
                dto.EventId,
                dto.EventGroups.Count,
                dto.EventResponders.Count,
                outboxMessages.Count);

            return TriggerEventResult.Ok(dto.EventId);
        }
        catch (DuplicateEventIdException ex)
        {
            // DB unique constraint 觸發（雙重保護，防止並發競爭條件）
            _logger.LogWarning("DB 唯一鍵觸發，EventId={EventId} 已存在", ex.EventId);
            return TriggerEventResult.Conflict(ex.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "事件觸發失敗：EventId={EventId}", dto.EventId);
            throw;
        }
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────────

    private static List<OutboxMessage> BuildOutboxMessages(
        HisEventTriggerDto dto, DateTime now)
    {
        var messages = new List<OutboxMessage>();

        messages.Add(new OutboxMessage
        {
            EventId     = dto.EventId,
            MessageType = OutboxMessageType.CreateTeam,
            PayloadJson = JsonSerializer.Serialize(new { event_id = dto.EventId }),
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = now
        });

        foreach (var group in dto.EventGroups)
        {
            messages.Add(new OutboxMessage
            {
                EventId     = dto.EventId,
                MessageType = OutboxMessageType.CreateChat,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    event_id = dto.EventId,
                    chat_gp  = group.ChatGp
                }),
                Status    = OutboxMessageStatus.Pending,
                CreatedAt = now
            });
        }

        messages.Add(new OutboxMessage
        {
            EventId     = dto.EventId,
            MessageType = OutboxMessageType.SendFlexMessage,
            PayloadJson = JsonSerializer.Serialize(new { event_id = dto.EventId }),
            Status      = OutboxMessageStatus.Pending,
            CreatedAt   = now
        });

        return messages;
    }

    private static HashSet<string> ParseJsonStringArray(string json)
    {
        try
        {
            return new HashSet<string>(
                JsonSerializer.Deserialize<List<string>>(json) ?? new(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(); }
    }

    private static (string? DisplayName, string? PhoneNumber) ParseDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (null, null);

        var match = System.Text.RegularExpressions.Regex.Match(
            description, @"\((\d{6,12})\)$");

        if (match.Success)
        {
            var phone       = match.Groups[1].Value;
            var displayName = description[..match.Index].TrimEnd('-').Trim();
            return (displayName.Length > 0 ? displayName : null, phone);
        }

        return (description, null);
    }
}
