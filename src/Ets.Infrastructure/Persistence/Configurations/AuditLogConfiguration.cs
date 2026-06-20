using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// AuditLogs 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.8 資料表定義。
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.AuditId);
        builder.Property(a => a.AuditId)
            .HasColumnName("AuditID")
            .UseIdentityColumn();

        builder.Property(a => a.Category)
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        builder.Property(a => a.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)");

        builder.Property(a => a.Actor)
            .HasColumnType("VARCHAR(100)");

        builder.Property(a => a.Action)
            .HasColumnType("VARCHAR(100)")
            .IsRequired();

        // Detail 中的個資（電話等）應在寫入前遮罩
        builder.Property(a => a.Detail)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(a => a.HttpStatus)
            .HasColumnType("INT");

        builder.Property(a => a.DurationMs)
            .HasColumnType("INT");

        builder.Property(a => a.CreatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(a => new { a.EventId, a.CreatedAt })
            .HasDatabaseName("IX_AuditLogs_Event_Created")
            .IsDescending(false, true);
    }
}
