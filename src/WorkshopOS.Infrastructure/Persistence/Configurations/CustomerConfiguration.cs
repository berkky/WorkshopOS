using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Customers;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Email).HasMaxLength(320);
        builder.Property(entity => entity.Phone).HasMaxLength(50);
        builder.Property(entity => entity.Notes).HasMaxLength(4000);
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.ConfigureTimestamps();
    }
}
