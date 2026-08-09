using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Estimates;

public class EstimateItem : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid EstimateId { get; private set; }

    public EstimateItemType Type { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public bool? IsCustomerApproved { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected EstimateItem()
    {
    }

    public EstimateItem(
        Guid organizationId,
        Guid estimateId,
        EstimateItemType type,
        string description,
        decimal quantity,
        decimal unitPrice,
        int sortOrder = 0,
        bool? isCustomerApproved = null)
        : base(organizationId)
    {
        if (estimateId == Guid.Empty)
        {
            throw new ArgumentException("Estimate identifier is required.", nameof(estimateId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Estimate item description is required.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        EstimateId = estimateId;
        Type = type;
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        SortOrder = sortOrder;
        IsCustomerApproved = isCustomerApproved;
    }
}
