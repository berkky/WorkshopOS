using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Billing;

public class Invoice : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid RepairOrderId { get; private set; }

    public Guid? SourceEstimateId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public InvoiceStatus Status { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public DateTimeOffset? IssuedAtUtc { get; private set; }

    public DateTimeOffset? VoidedAtUtc { get; private set; }

    public string? CommercialNotes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Invoice()
    {
    }

    public Invoice(
        Guid organizationId,
        Guid repairOrderId,
        string number,
        string currencyCode,
        Guid? sourceEstimateId = null,
        string? commercialNotes = null)
        : base(organizationId)
    {
        if (repairOrderId == Guid.Empty)
        {
            throw new ArgumentException("Repair order identifier is required.", nameof(repairOrderId));
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Invoice number is required.", nameof(number));
        }

        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        }

        RepairOrderId = repairOrderId;
        SourceEstimateId = sourceEstimateId;
        Number = number.Trim().ToUpperInvariant();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        Status = InvoiceStatus.Draft;
        CommercialNotes = string.IsNullOrWhiteSpace(commercialNotes) ? null : commercialNotes.Trim();
    }

    public void UpdateCommercialNotes(string? commercialNotes)
    {
        EnsureDraft();

        CommercialNotes = string.IsNullOrWhiteSpace(commercialNotes) ? null : commercialNotes.Trim();
    }

    public void Issue(DateTimeOffset issuedAtUtc)
    {
        EnsureDraft();

        Status = InvoiceStatus.Issued;
        IssuedAtUtc = issuedAtUtc;
    }

    public void Void(DateTimeOffset voidedAtUtc)
    {
        if (Status == InvoiceStatus.Voided)
        {
            throw new InvalidOperationException("Invoice is already voided.");
        }

        Status = InvoiceStatus.Voided;
        VoidedAtUtc = voidedAtUtc;
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only draft invoices can be edited.");
        }
    }
}
