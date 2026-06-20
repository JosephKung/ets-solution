using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// WebhookInbox 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.6 資料表定義。
/// </summary>
public class WebhookInboxConfiguration : IEntityTypeConfiguration<WebhookInbox>
{
    public void Configure(EntityTypeBuilder<WebhookInbox> builder)
    {
        builder.ToTable("WebhookInbox");

        builder.HasKey(w => w.InboxId);
        builder.Property(w => w.InboxId)
            .HasColumnName("InboxID")
            .UseIdentityColumn();

        builder.Property(w => w.Source)
            .HasColumnType("VARCHAR(20)")
            .IsRequired();

        builder.Property(w => w.ExternalMessageId)
            .HasColumnName("ExternalMessageID")
            .HasColumnType("VARCHAR(100)")
            .IsRequired();

        builder.Property(w => w.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)");

        builder.Property(w => w.Account)
            .HasColumnType("VARCHAR(50)");

        builder.Property(w => w.RawPayload)
            .HasColumnType("NVARCHAR(MAX)")
            .IsRequired();

        builder.Property(w => w.SignatureValid)
            .HasColumnType("BIT")
            .IsRequired();

        builder.Property(w => w.ReceivedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(w => w.ProcessedAt)
            .HasColumnType("DATETIME2(3)");

        // 冪等唯一約束：同一來源的同一訊息只能處理一次
        builder.HasIndex(w => new { w.Source, w.ExternalMessageId })
            .HasDatabaseName("UQ_WebhookInbox_Source_ExtID")
            .IsUnique();
    }
}
