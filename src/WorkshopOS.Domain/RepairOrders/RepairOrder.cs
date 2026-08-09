using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.RepairOrders;

public class RepairOrder : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid WorkshopLocationId { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid VehicleId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public RepairOrderStatus Status { get; private set; }

    public string? CustomerConcern { get; private set; }

    public string? InternalNotes { get; private set; }

    public int? Odometer { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected RepairOrder()
    {
    }

    public RepairOrder(
        Guid organizationId,
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        string number,
        DateTimeOffset openedAtUtc,
        RepairOrderStatus status = RepairOrderStatus.Draft,
        Guid? appointmentId = null,
        string? customerConcern = null,
        string? internalNotes = null,
        int? odometer = null)
        : base(organizationId)
    {
        if (workshopLocationId == Guid.Empty)
        {
            throw new ArgumentException("Workshop location identifier is required.", nameof(workshopLocationId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer identifier is required.", nameof(customerId));
        }

        if (vehicleId == Guid.Empty)
        {
            throw new ArgumentException("Vehicle identifier is required.", nameof(vehicleId));
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Repair order number is required.", nameof(number));
        }

        WorkshopLocationId = workshopLocationId;
        AppointmentId = appointmentId;
        CustomerId = customerId;
        VehicleId = vehicleId;
        Number = number.Trim();
        Status = status;
        OpenedAtUtc = openedAtUtc;
        CustomerConcern = string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();
        InternalNotes = string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();
        Odometer = odometer;
    }
}
