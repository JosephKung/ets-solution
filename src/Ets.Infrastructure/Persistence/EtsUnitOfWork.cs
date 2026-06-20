using Ets.Application.Abstractions;
using Ets.Application.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ets.Infrastructure.Persistence;

/// <summary>
/// EF Core 實作的 Unit of Work。
/// SaveChangesAsync 會捕捉 DB 唯一鍵違反並轉換為 DuplicateEventIdException，
/// 讓 Application 層不需依賴 EF Core。
/// </summary>
public sealed class EtsUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private IDbContextTransaction? _currentTransaction;

    public EtsUnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (EtsRepository.IsDuplicateKeyException(ex))
        {
            // 取得觸發衝突的 EventId（若追蹤中有 EmergencyEvent）
            var eventId = _db.ChangeTracker.Entries<Domain.Entities.EmergencyEvent>()
                             .FirstOrDefault()?.Entity.EventId ?? "unknown";
            throw new DuplicateEventIdException(eventId);
        }
    }

    public async Task<IAsyncDisposable> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        _currentTransaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return _currentTransaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
            throw new InvalidOperationException("No active transaction to commit");

        await _currentTransaction.CommitAsync(cancellationToken);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }
}
