using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Estimates;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class EstimateItemConfiguration : IEntityTypeConfiguration<EstimateItem>
{
    public void Configure(EntityTypeBuilder<EstimateItem> builder)
    {
        builder.ToTable("EstimateItems");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Type).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(12, 3).IsRequired();
        builder.Property(entity => entity.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.SortOrder).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasOne<Estimate>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.EstimateId })
            .HasPrincipalKey(estimate => new { estimate.OrganizationId, estimate.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.EstimateId,
            entity.SortOrder,
        });
    }
}
