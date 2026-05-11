using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.ControlPlane.Domain;

namespace ShopFlow.ControlPlane.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the <c>tenants</c> table per Tech Design v3.0 §1.5.
/// Column layout (snake_case per AGENTS.md §7.50):
/// <c>id, slug, db_name, region, tier, status, business_reg, sub_processors,
/// created_at, updated_at, provisioned_at, archiving_at, archived_at,
/// breach_notified_at, last_failure_reason, row_version</c>.
/// </summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();

        builder.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");

        builder.Property(t => t.DbName).HasColumnName("db_name").HasMaxLength(128).IsRequired();

        builder.HasIndex(t => t.DbName).IsUnique().HasDatabaseName("ux_tenants_db_name");

        builder.Property(t => t.Region).HasColumnName("region").HasMaxLength(32).IsRequired();

        builder.Property(t => t.Tier).HasColumnName("tier").HasMaxLength(32).IsRequired();

        builder
            .Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder
            .Property(t => t.BusinessRegistration)
            .HasColumnName("business_reg")
            .HasMaxLength(128);

        builder
            .Property(t => t.SubProcessorsJson)
            .HasColumnName("sub_processors")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.Property(t => t.ProvisionedAt).HasColumnName("provisioned_at");

        builder.Property(t => t.ArchivingAt).HasColumnName("archiving_at");

        builder.Property(t => t.ArchivedAt).HasColumnName("archived_at");

        builder.Property(t => t.BreachNotifiedAt).HasColumnName("breach_notified_at");

        builder
            .Property(t => t.LastFailureReason)
            .HasColumnName("last_failure_reason")
            .HasMaxLength(2048);

        builder
            .Property(t => t.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .HasColumnType("xid")
            .HasDefaultValueSql("(txid_current())::text::xid");
    }
}
