using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.RepairOrderId).IsRequired();
        builder.Property(entity => entity.SourceEstimateId);
        builder.Property(entity => entity.Number).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Status).IsRequired();
        builder.Property(entity => entity.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(entity => entity.IssuedAtUtc);
        builder.Property(entity => entity.VoidedAtUtc);
        builder.Property(entity => entity.CommercialNotes).HasMaxLength(2000);

        builder.ConfigureTimestamps();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.Number }).IsUnique();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.RepairOrderId })
            .IsUnique()
            .HasFilter("\"VoidedAtUtc\" IS NULL");

        builder.HasIndex(entity => new { entity.OrganizationId, entity.SourceEstimateId })
            .IsUnique()
            .HasFilter("\"SourceEstimateId\" IS NOT NULL AND \"VoidedAtUtc\" IS NULL");

        builder.HasOne<RepairOrder>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RepairOrderId })
            .HasPrincipalKey(repairOrder => new { repairOrder.OrganizationId, repairOrder.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Estimate>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.SourceEstimateId })
            .HasPrincipalKey(estimate => new { estimate.OrganizationId, estimate.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
