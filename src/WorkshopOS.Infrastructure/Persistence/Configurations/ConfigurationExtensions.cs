using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Common;
using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public static void ConfigureOrganizationOwnedPrincipal<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : OrganizationOwnedEntity
    {
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.OrganizationId).IsRequired();

        builder.HasAlternateKey(entity => new { entity.OrganizationId, entity.Id });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(entity => entity.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static void ConfigureTimestamps<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IHasTimestamps
    {
        builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc).IsRequired();
    }
}
