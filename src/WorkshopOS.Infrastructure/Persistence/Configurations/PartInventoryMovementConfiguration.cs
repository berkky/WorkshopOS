using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

public sealed class PartInventoryMovementConfiguration : IEntityTypeConfiguration<PartInventoryMovement>
{
    public void Configure(EntityTypeBuilder<PartInventoryMovement> builder)
    {
        builder.ToTable("PartInventoryMovements");
        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.PartCatalogItemId).IsRequired();
        builder.Property(entity => entity.WorkshopLocationId).IsRequired();
        builder.Property(entity => entity.MovementType).IsRequired();
        builder.Property(entity => entity.QuantityDelta).HasPrecision(12, 3).IsRequired();
        builder.Property(entity => entity.BalanceAfter).HasPrecision(12, 3).IsRequired();
        builder.Property(entity => entity.Reason).HasMaxLength(500);
        builder.Property(entity => entity.RecordedByUserId).IsRequired();
        builder.Property(entity => entity.OccurredAtUtc).IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).IsRequired();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.PartCatalogItemId, entity.WorkshopLocationId, entity.OccurredAtUtc });

        builder.HasOne<PartInventoryBalance>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.PartCatalogItemId, entity.WorkshopLocationId })
            .HasPrincipalKey(balance => new { balance.OrganizationId, balance.PartCatalogItemId, balance.WorkshopLocationId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RecordedByUserId })
            .HasPrincipalKey(membership => new { membership.OrganizationId, membership.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
