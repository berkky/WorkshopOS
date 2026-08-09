using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Vehicles;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class RepairOrderConfiguration : IEntityTypeConfiguration<RepairOrder>
{
    public void Configure(EntityTypeBuilder<RepairOrder> builder)
    {
        builder.ToTable("RepairOrders");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Number).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.CustomerConcern).HasMaxLength(2000);
        builder.Property(entity => entity.InternalNotes).HasMaxLength(4000);
        builder.Property(entity => entity.OpenedAtUtc).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.Number }).IsUnique();

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

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.AppointmentId })
            .HasPrincipalKey(appointment => new { appointment.OrganizationId, appointment.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.WorkshopLocationId,
            entity.Status,
        });
    }
}
