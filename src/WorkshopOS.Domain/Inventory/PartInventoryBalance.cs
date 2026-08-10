using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Inventory;

public class PartInventoryBalance : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid PartCatalogItemId { get; private set; }

    public Guid WorkshopLocationId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected PartInventoryBalance()
    {
    }

    public PartInventoryBalance(
        Guid organizationId,
        Guid partCatalogItemId,
        Guid workshopLocationId,
        decimal quantityOnHand = 0m)
        : base(organizationId)
    {
        if (partCatalogItemId == Guid.Empty)
        {
            throw new ArgumentException("Part catalog item identifier is required.", nameof(partCatalogItemId));
        }

        if (workshopLocationId == Guid.Empty)
        {
            throw new ArgumentException("Workshop location identifier is required.", nameof(workshopLocationId));
        }

        if (quantityOnHand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityOnHand), "Quantity on hand cannot be negative.");
        }

        PartCatalogItemId = partCatalogItemId;
        WorkshopLocationId = workshopLocationId;
        QuantityOnHand = quantityOnHand;
    }

    public void ApplyDelta(decimal quantityDelta)
    {
        var newQuantity = QuantityOnHand + quantityDelta;
        if (newQuantity < 0)
        {
            throw new InvalidOperationException("Inventory quantity cannot become negative.");
        }

        QuantityOnHand = newQuantity;
    }
}
