using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("Inspections");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.TechnicianNotes).HasMaxLength(4000);
        builder.ConfigureTimestamps();

        builder.HasOne<RepairOrder>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RepairOrderId })
            .HasPrincipalKey(repairOrder => new { repairOrder.OrganizationId, repairOrder.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
