using Ets.Application.Abstractions;
using Ets.Application.Exceptions;
using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ets.Infrastructure.Persistence;

/// <summary>
/// EF Core 實作的 ETS Repository。
/// 負責捕捉 DB 層例外（如唯一鍵違反）並轉換為 Domain Exception，
/// 確保 Application 層不需依賴 EF Core / SqlClient。
/// </summary>
public sealed class EtsRepository : IEtsRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<EtsRepository> _logger;

    public EtsRepository(AppDbContext db, ILogger<EtsRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public Task<bool> EventExistsAsync(string eventId, CancellationToken ct = default)
        => _db.EmergencyEvents.AsNoTracking()
               .AnyAsync(e => e.EventId == eventId, ct);

    public Task AddEventAsync(EmergencyEvent ev, CancellationToken ct = default)
    {
        _db.EmergencyEvents.Add(ev);
        return Task.CompletedTask;
    }

    public Task AddGroupAsync(EventGroup group, CancellationToken ct = default)
    {
        _db.EventGroups.Add(group);
        return Task.CompletedTask;
    }

    public Task AddResponderAsync(EventResponder responder, CancellationToken ct = default)
    {
        _db.EventResponders.Add(responder);
        return Task.CompletedTask;
    }

    public Task AddOutboxMessageAsync(OutboxMessage message, CancellationToken ct = default)
    {
        _db.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 捕捉 SaveChanges 時的 DB 唯一鍵違反，轉換為 DuplicateEventIdException。
    /// 由 EtsUnitOfWork.SaveChangesAsync 呼叫。
    /// </summary>
    public static bool IsDuplicateKeyException(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
           && (sqlEx.Number == 2627 || sqlEx.Number == 2601);
}
