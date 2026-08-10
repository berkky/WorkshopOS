using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Billing;

public class InvoicePaymentRecord : OrganizationOwnedEntity
{
    public Guid InvoiceId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public string? Reference { get; private set; }

    public string? Note { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    protected InvoicePaymentRecord()
    {
    }

    public InvoicePaymentRecord(
        Guid organizationId,
        Guid invoiceId,
        decimal amount,
        PaymentMethod paymentMethod,
        Guid recordedByUserId,
        DateTimeOffset recordedAtUtc,
        string? reference = null,
        string? note = null)
        : base(organizationId)
    {
        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice identifier is required.", nameof(invoiceId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        }

        if (recordedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Recorded-by user identifier is required.", nameof(recordedByUserId));
        }

        InvoiceId = invoiceId;
        Amount = amount;
        PaymentMethod = paymentMethod;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = recordedAtUtc;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreatedAtUtc = recordedAtUtc;
    }
}
