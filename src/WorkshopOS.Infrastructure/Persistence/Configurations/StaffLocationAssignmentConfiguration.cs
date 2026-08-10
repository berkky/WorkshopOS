using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Staff;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class StaffLocationAssignmentConfiguration : IEntityTypeConfiguration<StaffLocationAssignment>
{
    public void Configure(EntityTypeBuilder<StaffLocationAssignment> builder)
    {
        builder.ToTable("StaffLocationAssignments");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.StaffMemberId).IsRequired();
        builder.Property(entity => entity.WorkshopLocationId).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new
        {
            entity.OrganizationId,
            entity.StaffMemberId,
            entity.WorkshopLocationId,
        }).IsUnique();

        builder.HasOne<StaffMember>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.StaffMemberId })
            .HasPrincipalKey(staff => new { staff.OrganizationId, staff.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkshopLocation>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.WorkshopLocationId })
            .HasPrincipalKey(location => new { location.OrganizationId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
