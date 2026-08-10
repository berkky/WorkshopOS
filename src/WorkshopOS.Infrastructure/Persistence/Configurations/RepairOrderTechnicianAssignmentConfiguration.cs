using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class RepairOrderTechnicianAssignmentConfiguration
    : IEntityTypeConfiguration<RepairOrderTechnicianAssignment>
{
    public void Configure(EntityTypeBuilder<RepairOrderTechnicianAssignment> builder)
    {
        builder.ToTable("RepairOrderTechnicianAssignments");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.RepairOrderId).IsRequired();
        builder.Property(entity => entity.StaffMemberId).IsRequired();
        builder.Property(entity => entity.WorkStatus).IsRequired();
        builder.Property(entity => entity.AssignedAtUtc).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.RepairOrderId })
            .IsUnique()
            .HasFilter("\"UnassignedAtUtc\" IS NULL");

        builder.HasIndex(entity => new { entity.OrganizationId, entity.StaffMemberId });

        builder.HasOne<RepairOrder>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RepairOrderId })
            .HasPrincipalKey(repairOrder => new { repairOrder.OrganizationId, repairOrder.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StaffMember>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.StaffMemberId })
            .HasPrincipalKey(staffMember => new { staffMember.OrganizationId, staffMember.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
