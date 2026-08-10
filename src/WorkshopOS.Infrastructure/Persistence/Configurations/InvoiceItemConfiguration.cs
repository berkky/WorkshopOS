using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Billing;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

public sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("InvoiceItems");
        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.InvoiceId).IsRequired();
        builder.Property(entity => entity.Type).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(12, 3).IsRequired();
        builder.Property(entity => entity.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.SortOrder).IsRequired();

        builder.ConfigureTimestamps();

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.InvoiceId })
            .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
