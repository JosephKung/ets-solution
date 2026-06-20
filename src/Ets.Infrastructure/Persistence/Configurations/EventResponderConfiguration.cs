using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// EventResponders 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.3 資料表定義。
/// </summary>
public class EventResponderConfiguration : IEntityTypeConfiguration<EventResponder>
{
    public void Configure(EntityTypeBuilder<EventResponder> builder)
    {
        builder.ToTable("EventResponders");

        builder.HasKey(r => r.ResponderId);
        builder.Property(r => r.ResponderId)
            .HasColumnName("ResponderID")
            .UseIdentityColumn();

        builder.Property(r => r.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        builder.Property(r => r.Account)
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        builder.Property(r => r.DisplayName)
            .HasColumnType("NVARCHAR(100)");

        builder.Property(r => r.PhoneNumber)
            .HasColumnType("VARCHAR(20)");

        builder.Property(r => r.Description)
            .HasColumnType("NVARCHAR(200)");

        builder.Property(r => r.Role)
            .HasColumnType("VARCHAR(20)")
            .IsRequired();

        builder.Property(r => r.ChatGp)
            .HasColumnName("ChatGP")
            .HasColumnType("NVARCHAR(100)")
            .IsRequired();

        // ── 四大狀態欄位 ──────────────────────────────────────────────────
        builder.Property(r => r.ReadStatus)
            .HasColumnType("VARCHAR(20)");

        builder.Property(r => r.ReplyStatus)
            .HasColumnType("NVARCHAR(50)")
            .IsRequired()
            .HasDefaultValue("Pending");

        builder.Property(r => r.ReplyChannel)
            .HasColumnType("VARCHAR(20)")
            .IsRequired()
            .HasDefaultValue("None");

        builder.Property(r => r.LastVoiceStatus)
            .HasColumnType("VARCHAR(20)");

        builder.Property(r => r.LastVoiceStatusAt)
            .HasColumnType("DATETIME2(3)");

        builder.Property(r => r.CheckInStatus)
            .HasColumnType("BIT")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.CheckInAt)
            .HasColumnType("DATETIME2(3)");

        // ── team+ 整合欄位 ────────────────────────────────────────────────
        builder.Property(r => r.FlexMessageSn)
            .HasColumnName("FlexMessageSN")
            .HasColumnType("INT");

        // ── 語音欄位 ──────────────────────────────────────────────────────
        builder.Property(r => r.VoiceRetryCount)
            .HasColumnType("INT")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.LastVoiceCallTime)
            .HasColumnType("DATETIME2(3)");

        builder.Property(r => r.LastExternalCallId)
            .HasColumnName("LastExternalCallID")
            .HasColumnType("VARCHAR(100)");

        // ── 臨時成員欄位 ──────────────────────────────────────────────────
        builder.Property(r => r.IsAdHoc)
            .HasColumnType("BIT")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.AdHocApprovedBy)
            .HasColumnType("VARCHAR(50)");

        // ── 時間戳 ────────────────────────────────────────────────────────
        builder.Property(r => r.CreatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(r => r.UpdatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // ── 唯一約束 ──────────────────────────────────────────────────────
        builder.HasIndex(r => new { r.EventId, r.Account })
            .HasDatabaseName("UQ_EventResponders_Event_Account")
            .IsUnique();

        // ── 索引 ──────────────────────────────────────────────────────────
        builder.HasIndex(r => r.EventId)
            .HasDatabaseName("IX_EventResponders_EventID");

        builder.HasIndex(r => new { r.EventId, r.FlexMessageSn })
            .HasDatabaseName("IX_EventResponders_Event_FlexMsgSN");

        builder.HasIndex(r => new { r.EventId, r.ChatGp })
            .HasDatabaseName("IX_EventResponders_Event_ChatGP");

        builder.HasIndex(r => r.LastExternalCallId)
            .HasDatabaseName("IX_EventResponders_LastExternalCallID")
            .HasFilter("[LastExternalCallID] IS NOT NULL");

        builder.HasIndex(r => new { r.EventId, r.LastVoiceStatus })
            .HasDatabaseName("IX_EventResponders_Event_VoiceStatus")
            .HasFilter("[LastVoiceStatus] IS NOT NULL");
    }
}
