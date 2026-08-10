using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

public sealed class PartInventoryBalanceConfiguration : IEntityTypeConfiguration<PartInventoryBalance>
{
    public void Configure(EntityTypeBuilder<PartInventoryBalance> builder)
    {
        builder.ToTable("PartInventoryBalances");
        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.PartCatalogItemId).IsRequired();
        builder.Property(entity => entity.WorkshopLocationId).IsRequired();
        builder.Property(entity => entity.QuantityOnHand).HasPrecision(12, 3).IsRequired();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PartInventoryBalances_QuantityOnHand_NonNegative",
            "\"QuantityOnHand\" >= 0"));

        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.PartCatalogItemId, entity.WorkshopLocationId })
            .IsUnique();

        builder.HasOne<PartCatalogItem>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.PartCatalogItemId })
            .HasPrincipalKey(part => new { part.OrganizationId, part.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkshopLocation>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.WorkshopLocationId })
            .HasPrincipalKey(location => new { location.OrganizationId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
