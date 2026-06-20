using Ets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ets.Infrastructure.Persistence.Configurations;

/// <summary>
/// TeamPlusVirtualAccounts 資料表 EF Core Fluent API 設定。
/// 對應規格書 §6.6 虛擬帳號機制。
/// </summary>
public class TeamPlusVirtualAccountConfiguration : IEntityTypeConfiguration<TeamPlusVirtualAccount>
{
    public void Configure(EntityTypeBuilder<TeamPlusVirtualAccount> builder)
    {
        builder.ToTable("TeamPlusVirtualAccounts");

        builder.HasKey(t => t.AccountId);
        builder.Property(t => t.AccountId)
            .HasColumnName("AccountID")
            .UseIdentityColumn();

        builder.Property(t => t.Account)
            .HasColumnType("VARCHAR(100)")
            .IsRequired();

        // AES-256-GCM 加密後的 api_key，最大 256 bytes
        builder.Property(t => t.EncryptedApiKey)
            .HasColumnType("VARBINARY(512)")
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnType("NVARCHAR(200)");

        builder.Property(t => t.IsActive)
            .HasColumnType("BIT")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(t => t.UpdatedAt)
            .HasColumnType("DATETIME2(3)")
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(t => t.Account)
            .HasDatabaseName("UQ_TeamPlusVirtualAccounts_Account")
            .IsUnique();
    }
}
