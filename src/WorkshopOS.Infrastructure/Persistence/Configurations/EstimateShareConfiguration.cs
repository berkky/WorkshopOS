using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence.Configurations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class EstimateShareConfiguration : IEntityTypeConfiguration<EstimateShare>
{
    public void Configure(EntityTypeBuilder<EstimateShare> builder)
    {
        builder.ToTable("EstimateShares");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.PublicId).IsRequired();
        builder.Property(entity => entity.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).IsRequired();
        builder.Property(entity => entity.CreatedByUserId).IsRequired();
        builder.Property(entity => entity.RevokedByUserId);
        builder.Property(entity => entity.Decision);
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => entity.PublicId).IsUnique();
        builder.HasIndex(entity => entity.TokenHash).IsUnique();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.EstimateId })
            .IsUnique()
            .HasFilter("\"RevokedAtUtc\" IS NULL");

        builder.HasOne<Estimate>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.EstimateId })
            .HasPrincipalKey(estimate => new { estimate.OrganizationId, estimate.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.CreatedByUserId })
            .HasPrincipalKey(membership => new { membership.OrganizationId, membership.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
