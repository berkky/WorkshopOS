using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Infrastructure.Persistence.Configurations;

public sealed class InvoicePaymentRecordConfiguration : IEntityTypeConfiguration<InvoicePaymentRecord>
{
    public void Configure(EntityTypeBuilder<InvoicePaymentRecord> builder)
    {
        builder.ToTable("InvoicePaymentRecords");
        builder.ConfigureOrganizationOwnedPrincipal();

        builder.Property(entity => entity.InvoiceId).IsRequired();
        builder.Property(entity => entity.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.PaymentMethod).IsRequired();
        builder.Property(entity => entity.Reference).HasMaxLength(100);
        builder.Property(entity => entity.Note).HasMaxLength(500);
        builder.Property(entity => entity.RecordedByUserId).IsRequired();
        builder.Property(entity => entity.RecordedAtUtc).IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).IsRequired();

        builder.HasIndex(entity => new { entity.OrganizationId, entity.InvoiceId, entity.RecordedAtUtc });

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.InvoiceId })
            .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(entity => new { entity.OrganizationId, entity.RecordedByUserId })
            .HasPrincipalKey(membership => new { membership.OrganizationId, membership.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
