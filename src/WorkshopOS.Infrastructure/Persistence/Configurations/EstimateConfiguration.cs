using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

internal sealed class EstimateConfiguration : IEntityTypeConfiguration<Estimate>
{
    public void Configure(EntityTypeBuilder<Estimate> builder)
    {
        builder.ToTable("Estimates");

        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.Number).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(entity => entity.CustomerMessage).HasMaxLength(2000);
        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.Number }).IsUnique();

        builder.HasOne<RepairOrder>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RepairOrderId })
            .HasPrincipalKey(repairOrder => new { repairOrder.OrganizationId, repairOrder.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
