using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// QrCheckInTokens 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.7 資料表定義。
/// </summary>
public class QrCheckInTokenConfiguration : IEntityTypeConfiguration<QrCheckInToken>
{
    public void Configure(EntityTypeBuilder<QrCheckInToken> builder)
    {
        builder.ToTable("QrCheckInTokens");

        builder.HasKey(t => t.TokenId);
        builder.Property(t => t.TokenId)
            .HasColumnName("TokenID")
            .HasColumnType("UNIQUEIDENTIFIER")
            .HasDefaultValueSql("NEWID()");

        builder.Property(t => t.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        builder.Property(t => t.Nonce)
            .HasColumnType("VARCHAR(64)")
            .IsRequired();

        builder.Property(t => t.Signature)
            .HasColumnType("VARCHAR(64)")
            .IsRequired();

        builder.Property(t => t.IssuedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(t => t.ExpiresAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired();

        builder.Property(t => t.RotatedAt)
            .HasColumnType("DATETIME2(3)");

        builder.Property(t => t.IsActive)
            .HasColumnType("BIT")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.GracePeriodEndAt)
            .HasColumnType("DATETIME2(3)");

        builder.HasIndex(t => t.Nonce)
            .HasDatabaseName("UQ_QrCheckInTokens_Nonce")
            .IsUnique();

        builder.HasIndex(t => new { t.EventId, t.IsActive, t.ExpiresAt })
            .HasDatabaseName("IX_QrCheckInTokens_Event_Active");

        builder.HasOne(t => t.Event)
            .WithMany()
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
