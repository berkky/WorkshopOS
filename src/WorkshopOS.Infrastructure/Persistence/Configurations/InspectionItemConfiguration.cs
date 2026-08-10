using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Inspections;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class InspectionItemConfiguration : IEntityTypeConfiguration<InspectionItem>
{
    public void Configure(EntityTypeBuilder<InspectionItem> builder)
    {
        builder.ToTable("InspectionItems");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Section).HasMaxLength(120).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Condition).IsRequired();
        builder.Property(entity => entity.Notes).HasMaxLength(2000);
        builder.Property(entity => entity.SortOrder).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasOne<Inspection>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.InspectionId })
            .HasPrincipalKey(inspection => new { inspection.OrganizationId, inspection.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.InspectionId,
            entity.SortOrder,
        });

        builder.HasAlternateKey(entity => new
        {
            entity.OrganizationId,
            entity.InspectionId,
            entity.Id,
        });
    }
}
