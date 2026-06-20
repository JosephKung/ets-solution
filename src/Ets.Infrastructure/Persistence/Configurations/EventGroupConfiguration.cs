using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// EventGroups 資料表 EF Core Fluent API 設定。
/// 對應規格書 §4.2 資料表定義。
/// </summary>
public class EventGroupConfiguration : IEntityTypeConfiguration<EventGroup>
{
    public void Configure(EntityTypeBuilder<EventGroup> builder)
    {
        builder.ToTable("EventGroups");

        builder.HasKey(g => g.GroupId);
        builder.Property(g => g.GroupId)
            .HasColumnName("GroupID")
            .UseIdentityColumn();

        builder.Property(g => g.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        // team+ 交談室名稱上限 20 字元，寫入前須截斷
        builder.Property(g => g.ChatGp)
            .HasColumnName("ChatGP")
            .HasColumnType("NVARCHAR(100)")
            .IsRequired();

        builder.Property(g => g.Description)
            .HasColumnType("NVARCHAR(200)");

        builder.Property(g => g.TeamPlusChatSn)
            .HasColumnName("TeamPlusChatSN")
            .HasColumnType("INT");

        builder.Property(g => g.CreatorAccount)
            .HasColumnType("VARCHAR(50)");

        builder.Property(g => g.MemberCount)
            .HasColumnType("INT")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(g => g.SplitGroupIndex)
            .HasColumnType("INT");

        builder.Property(g => g.CreatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // ── 唯一約束 ──────────────────────────────────────────────────────
        builder.HasIndex(g => new { g.EventId, g.ChatGp, g.SplitGroupIndex })
            .HasDatabaseName("UQ_EventGroups_Event_ChatGP")
            .IsUnique();

        builder.HasIndex(g => g.EventId)
            .HasDatabaseName("IX_EventGroups_EventID");
    }
}
