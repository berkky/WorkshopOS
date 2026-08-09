using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class WorkshopLocationConfiguration : IEntityTypeConfiguration<WorkshopLocation>
{
    public void Configure(EntityTypeBuilder<WorkshopLocation> builder)
    {
        builder.ToTable("WorkshopLocations");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Name).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Code).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.TimeZoneId).HasMaxLength(100);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.Code }).IsUnique();
    }
}
