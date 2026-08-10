using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Catalog;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

public sealed class ServiceCatalogItemConfiguration : IEntityTypeConfiguration<ServiceCatalogItem>
{
    public void Configure(EntityTypeBuilder<ServiceCatalogItem> builder)
    {
        builder.ToTable("ServiceCatalogItems");
        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Code).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(2000);
        builder.Property(entity => entity.DefaultUnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();

        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.Code }).IsUnique();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Name });
    }
}
