using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// OutboxMessages 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.4 資料表定義。
/// </summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(o => o.OutboxId);
        builder.Property(o => o.OutboxId)
            .HasColumnName("OutboxID")
            .UseIdentityColumn();

        builder.Property(o => o.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        // MessageType 儲存為字串（便於直接 SQL 查詢時可讀）
        builder.Property(o => o.MessageType)
            .HasColumnType("VARCHAR(50)")
            .IsRequired()
            .HasConversion<string>();

        builder.Property(o => o.PayloadJson)
            .HasColumnType("NVARCHAR(MAX)")
            .IsRequired();

        builder.Property(o => o.Status)
            .HasColumnType("TINYINT")
            .IsRequired()
            .HasDefaultValue(OutboxMessageStatus.Pending)
            .HasConversion<byte>();

        builder.Property(o => o.RetryCount)
            .HasColumnType("INT")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(o => o.LastError)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(o => o.NextRetryAt)
            .HasColumnType("DATETIME2(3)");

        builder.Property(o => o.ProcessedAt)
            .HasColumnType("DATETIME2(3)");

        builder.Property(o => o.CreatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // OutboxDispatcherWorker 核心查詢索引：篩選 Pending/Failed + 依 NextRetryAt 排序
        builder.HasIndex(o => new { o.Status, o.NextRetryAt })
            .HasDatabaseName("IX_Outbox_Status_NextRetry");
    }
}
