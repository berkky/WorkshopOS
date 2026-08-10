using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Billing;

public class InvoiceItem : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid InvoiceId { get; private set; }

    public InvoiceItemType Type { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected InvoiceItem()
    {
    }

    public InvoiceItem(
        Guid organizationId,
        Guid invoiceId,
        InvoiceItemType type,
        string description,
        decimal quantity,
        decimal unitPrice,
        int sortOrder = 0)
        : base(organizationId)
    {
        ValidateItemInputs(invoiceId, description, quantity, unitPrice);

        InvoiceId = invoiceId;
        Type = type;
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        SortOrder = sortOrder;
    }

    public void Update(
        InvoiceItemType type,
        string description,
        decimal quantity,
        decimal unitPrice,
        int sortOrder)
    {
        ValidateItemInputs(InvoiceId, description, quantity, unitPrice);

        Type = type;
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        SortOrder = sortOrder;
    }

    private static void ValidateItemInputs(
        Guid invoiceId,
        string description,
        decimal quantity,
        decimal unitPrice)
    {
        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice identifier is required.", nameof(invoiceId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Invoice item description is required.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }
    }
}
