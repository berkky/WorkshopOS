using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Staff;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("StaffMembers");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.UserId);
        builder.Property(entity => entity.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(entity => entity.Position).IsRequired();
        builder.Property(entity => entity.JobTitle).HasMaxLength(120);
        builder.Property(entity => entity.ContactEmail).HasMaxLength(256);
        builder.Property(entity => entity.PhoneNumber).HasMaxLength(50);
        builder.Property(entity => entity.Status).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.UserId })
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        builder.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.UserId })
            .HasPrincipalKey(membership => new { membership.OrganizationId, membership.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
