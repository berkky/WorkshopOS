using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Vehicles;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.ScheduledStartUtc).IsRequired();
        builder.Property(entity => entity.ScheduledEndUtc).IsRequired();
        builder.Property(entity => entity.CustomerConcern).HasMaxLength(2000);
        builder.Property(entity => entity.InternalNotes).HasMaxLength(4000);
        builder.ConfigureTimestamps();

        builder.HasOne<WorkshopLocation>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.WorkshopLocationId })
            .HasPrincipalKey(location => new { location.OrganizationId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.VehicleId })
            .HasPrincipalKey(vehicle => new { vehicle.OrganizationId, vehicle.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.WorkshopLocationId,
            entity.ScheduledStartUtc,
        });
    }
}
