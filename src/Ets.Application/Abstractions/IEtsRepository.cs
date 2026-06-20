using Ets.Domain.Entities;

namespace Ets.Application.Abstractions;

/// <summary>
/// ETS 核心資料存取介面。
/// Application 層透過此介面操作 DB，不直接依賴 EF Core。
/// </summary>
public interface IEtsRepository
{
    /// <summary>檢查指定 EventId 是否已存在</summary>
    Task<bool> EventExistsAsync(string eventId, CancellationToken ct = default);

    /// <summary>新增事件主檔</summary>
    Task AddEventAsync(EmergencyEvent ev, CancellationToken ct = default);

    /// <summary>新增分組</summary>
    Task AddGroupAsync(EventGroup group, CancellationToken ct = default);

    /// <summary>新增應變人員</summary>
    Task AddResponderAsync(EventResponder responder, CancellationToken ct = default);

    /// <summary>新增 Outbox 訊息</summary>
    Task AddOutboxMessageAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>新增稽核日誌</summary>
    Task AddAuditLogAsync(AuditLog auditLog, CancellationToken ct = default);

    // ── M5 新增 ───────────────────────────────────────────────────────────

    /// <summary>依 Nonce 查詢 QR Token（§8.5 Step 1）</summary>
    Task<QrCheckInToken?> FindQrTokenByNonceAsync(string nonce, CancellationToken ct = default);

    /// <summary>依 EventId 查詢事件主檔（§8.5 Step 4）</summary>
    Task<EmergencyEvent?> FindEventByIdAsync(string eventId, CancellationToken ct = default);

    /// <summary>依 EventId + Account 查詢應變人員（§8.5 Step 5）</summary>
    Task<EventResponder?> FindResponderByEventAndAccountAsync(
        string eventId, string account, CancellationToken ct = default);
}
