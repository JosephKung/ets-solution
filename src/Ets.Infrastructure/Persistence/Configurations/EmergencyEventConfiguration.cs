using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// EmergencyEvents 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.1 資料表定義。
/// </summary>
public class EmergencyEventConfiguration : IEntityTypeConfiguration<EmergencyEvent>
{
    public void Configure(EntityTypeBuilder<EmergencyEvent> builder)
    {
        builder.ToTable("EmergencyEvents");

        // ── 主鍵 ──────────────────────────────────────────────────────────
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        // ── 必填欄位 ──────────────────────────────────────────────────────
        builder.Property(e => e.EventType)
            .HasColumnType("VARCHAR(10)")
            .IsRequired();

        builder.Property(e => e.EventTime)
            .HasColumnType("DATETIME2(0)")
            .IsRequired();

        builder.Property(e => e.EventSummary)
            .HasColumnType("NVARCHAR(200)")
            .IsRequired();

        // ── 選填欄位 ──────────────────────────────────────────────────────
        builder.Property(e => e.EventDescription)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(e => e.EventArea)
            .HasColumnType("NVARCHAR(50)");

        builder.Property(e => e.AudioContent)
            .HasColumnType("NVARCHAR(500)");

        builder.Property(e => e.AudioFileName)
            .HasColumnType("VARCHAR(100)");

        builder.Property(e => e.EventSource)
            .HasColumnType("VARCHAR(50)")
            .IsRequired()
            .HasDefaultValue("HIS");

        builder.Property(e => e.FlexMsgItemsJson)
            .HasColumnType("NVARCHAR(500)");

        builder.Property(e => e.FlexMsgIntentMapJson)
            .HasColumnType("NVARCHAR(1000)");

        // ── 狀態欄位（Enum → TINYINT）────────────────────────────────────
        builder.Property(e => e.Status)
            .HasColumnType("TINYINT")
            .IsRequired()
            .HasDefaultValue(EventStatus.Processing)
            .HasConversion<byte>();

        // ── team+ 整合欄位 ────────────────────────────────────────────────
        builder.Property(e => e.TeamPlusBigTeamSn)
            .HasColumnName("TeamPlusBigTeamSN")
            .HasColumnType("INT");

        builder.Property(e => e.TeamPlusChannelId)
            .HasColumnName("TeamPlusChannelID")
            .HasColumnType("VARCHAR(50)");

        builder.Property(e => e.TeamPlusVirtualAccount)
            .HasColumnType("VARCHAR(100)");

        builder.Property(e => e.TeamPlusVirtualAccountApiKey)
            .HasColumnType("VARBINARY(256)");

        builder.Property(e => e.TeamPlusArticleBatchId)
            .HasColumnName("TeamPlusArticleBatchID")
            .HasColumnType("VARCHAR(50)");

        builder.Property(e => e.LastReadCount)
            .HasColumnType("INT");

        builder.Property(e => e.LastReadCountFetchAt)
            .HasColumnType("DATETIME2(3)");

        builder.Property(e => e.ClosedAt)
            .HasColumnType("DATETIME2(0)");

        // ── 時間戳欄位 ────────────────────────────────────────────────────
        builder.Property(e => e.CreatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // ── 索引 ──────────────────────────────────────────────────────────
        builder.HasIndex(e => new { e.Status, e.EventTime })
            .HasDatabaseName("IX_EmergencyEvents_Status_EventTime")
            .IsDescending(false, true);  // Status ASC, EventTime DESC

        builder.HasIndex(e => e.EventType)
            .HasDatabaseName("IX_EmergencyEvents_EventType");

        builder.HasIndex(e => new { e.EventArea, e.EventTime })
            .HasDatabaseName("IX_EmergencyEvents_EventArea_EventTime")
            .IsDescending(false, true);

        // ── 關聯設定 ──────────────────────────────────────────────────────
        builder.HasMany(e => e.Groups)
            .WithOne(g => g.Event)
            .HasForeignKey(g => g.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Responders)
            .WithOne(r => r.Event)
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.OutboxMessages)
            .WithOne(o => o.Event)
            .HasForeignKey(o => o.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
