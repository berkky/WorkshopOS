using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence.Configurations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class InspectionMediaAssetConfiguration : IEntityTypeConfiguration<InspectionMediaAsset>
{
    public void Configure(EntityTypeBuilder<InspectionMediaAsset> builder)
    {
        builder.ToTable("InspectionMediaAssets");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.StorageKey).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.MediaKind).IsRequired();
        builder.Property(entity => entity.ContentType).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Caption).HasMaxLength(500);
        builder.Property(entity => entity.LengthBytes).IsRequired();
        builder.Property(entity => entity.UploadedByUserId).IsRequired();
        builder.Property(entity => entity.UploadedAtUtc).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => entity.StorageKey).IsUnique();

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.InspectionId,
            entity.InspectionItemId,
        });

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.InspectionId,
            entity.RemovedAtUtc,
        });

        builder.HasOne<Inspection>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.InspectionId })
            .HasPrincipalKey(inspection => new { inspection.OrganizationId, inspection.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InspectionItem>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.OrganizationId,
                entity.InspectionId,
                entity.InspectionItemId,
            })
            .HasPrincipalKey(item => new
            {
                item.OrganizationId,
                item.InspectionId,
                item.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.UploadedByUserId })
            .HasPrincipalKey(membership => new { membership.OrganizationId, membership.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RemovedByUserId })
            .HasPrincipalKey(membership => new { membership.OrganizationId, membership.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
