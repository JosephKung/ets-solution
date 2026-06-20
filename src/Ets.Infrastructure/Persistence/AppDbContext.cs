using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ets.Infrastructure.Persistence;

/// <summary>
/// ETS 應用程式資料庫 Context。
/// 包含全部 10 個主要資料表的 DbSet，並套用對應的 IEntityTypeConfiguration。
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── DbSet（對應 10 個主要資料表）────────────────────────────────────

    /// <summary>緊急事件主檔</summary>
    public DbSet<EmergencyEvent> EmergencyEvents => Set<EmergencyEvent>();

    /// <summary>事件分組交談室</summary>
    public DbSet<EventGroup> EventGroups => Set<EventGroup>();

    /// <summary>應變人員狀態明細</summary>
    public DbSet<EventResponder> EventResponders => Set<EventResponder>();

    /// <summary>Outbox Pattern 派送箱</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Webhook 接收冪等紀錄</summary>
    public DbSet<WebhookInbox> WebhookInboxes => Set<WebhookInbox>();

    /// <summary>QR 報到 Token</summary>
    public DbSet<QrCheckInToken> QrCheckInTokens => Set<QrCheckInToken>();

    /// <summary>稽核日誌</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>臨時成員申請</summary>
    public DbSet<AdHocRequest> AdHocRequests => Set<AdHocRequest>();

    /// <summary>team+ 虛擬帳號</summary>
    public DbSet<TeamPlusVirtualAccount> TeamPlusVirtualAccounts => Set<TeamPlusVirtualAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 自動套用此 Assembly 中所有 IEntityTypeConfiguration<T> 實作
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
