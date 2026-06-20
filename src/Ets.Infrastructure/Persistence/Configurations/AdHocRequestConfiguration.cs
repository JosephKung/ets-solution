using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// AdHocRequests 資料表 EF Core Fluent API 設定。
/// 對應規格書 §10.2.11 臨時成員申請流程。
/// </summary>
public class AdHocRequestConfiguration : IEntityTypeConfiguration<AdHocRequest>
{
    public void Configure(EntityTypeBuilder<AdHocRequest> builder)
    {
        builder.ToTable("AdHocRequests");

        builder.HasKey(a => a.RequestId);
        builder.Property(a => a.RequestId)
            .HasColumnName("RequestID")
            .UseIdentityColumn();

        builder.Property(a => a.EventId)
            .HasColumnName("EventID")
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        builder.Property(a => a.Account)
            .HasColumnType("VARCHAR(50)")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnType("VARCHAR(20)")
            .IsRequired()
            .HasDefaultValue(AdHocRequestStatus.Pending)
            .HasConversion<string>();

        builder.Property(a => a.ApprovedBy)
            .HasColumnType("VARCHAR(50)");

        builder.Property(a => a.AssignedChatGp)
            .HasColumnName("AssignedChatGP")
            .HasColumnType("NVARCHAR(100)");

        builder.Property(a => a.RequestedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(a => a.DecidedAt)
            .HasColumnType("DATETIME2(3)");

        builder.HasIndex(a => new { a.EventId, a.Status })
            .HasDatabaseName("IX_AdHocRequests_Event_Status");

        builder.HasOne(a => a.Event)
            .WithMany()
            .HasForeignKey(a => a.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
