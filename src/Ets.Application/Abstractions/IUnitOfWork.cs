namespace Ets.Application.Abstractions;

/// <summary>
/// Unit of Work 介面。
/// 供 Application 層 Command Handler 執行 DB 交易，
/// 隔離 EF Core 細節，保持 Application 層對 Infrastructure 的依賴反轉。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// 儲存所有待定變更至資料庫。
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 開始一個資料庫交易，回傳 IAsyncDisposable 交易物件。
    /// using await 可確保例外時自動 Rollback。
    /// </summary>
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交目前的交易。
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
