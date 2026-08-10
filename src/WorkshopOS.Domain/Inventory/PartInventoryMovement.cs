using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Inventory;

public class PartInventoryMovement : OrganizationOwnedEntity
{
    public Guid PartCatalogItemId { get; private set; }

    public Guid WorkshopLocationId { get; private set; }

    public PartInventoryMovementType MovementType { get; private set; }

    public decimal QuantityDelta { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public string? Reason { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    protected PartInventoryMovement()
    {
    }

    public PartInventoryMovement(
        Guid organizationId,
        Guid partCatalogItemId,
        Guid workshopLocationId,
        PartInventoryMovementType movementType,
        decimal quantityDelta,
        decimal balanceAfter,
        Guid recordedByUserId,
        DateTimeOffset occurredAtUtc,
        string? reason = null)
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

        if (recordedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Recorded-by user identifier is required.", nameof(recordedByUserId));
        }

        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Quantity delta cannot be zero.");
        }

        if (balanceAfter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(balanceAfter), "Balance after cannot be negative.");
        }

        ValidateDeltaSign(movementType, quantityDelta);

        PartCatalogItemId = partCatalogItemId;
        WorkshopLocationId = workshopLocationId;
        MovementType = movementType;
        QuantityDelta = quantityDelta;
        BalanceAfter = balanceAfter;
        RecordedByUserId = recordedByUserId;
        OccurredAtUtc = occurredAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CreatedAtUtc = occurredAtUtc;
    }

    private static void ValidateDeltaSign(PartInventoryMovementType movementType, decimal quantityDelta)
    {
        switch (movementType)
        {
            case PartInventoryMovementType.OpeningBalance:
            case PartInventoryMovementType.ManualIncrease:
                if (quantityDelta <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Increase movements require a positive quantity delta.");
                }

                break;

            case PartInventoryMovementType.ManualDecrease:
                if (quantityDelta >= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Decrease movements require a negative quantity delta.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(movementType), movementType, "Unsupported movement type.");
        }
    }
}
