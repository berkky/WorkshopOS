using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Vehicles;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Vin).HasMaxLength(32);
        builder.Property(entity => entity.RegistrationPlate).HasMaxLength(32);
        builder.Property(entity => entity.Make).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Model).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Color).HasMaxLength(60);
        builder.ConfigureTimestamps();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.CurrentCustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
