using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence.Configurations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("OrganizationMemberships");

        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.OrganizationId).IsRequired();
        builder.Property(entity => entity.UserId).IsRequired();
        builder.Property(entity => entity.Role).IsRequired();
        builder.Property(entity => entity.Status).IsRequired();
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.UserId }).IsUnique();
        builder.HasAlternateKey(entity => new { entity.OrganizationId, entity.UserId });
        builder.HasIndex(entity => new { entity.UserId, entity.Status });
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Status });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(entity => entity.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
