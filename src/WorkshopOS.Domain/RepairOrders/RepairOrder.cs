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

    public RepairOrderPriority Priority { get; private set; }

    public string? CustomerConcern { get; private set; }

    public string? InternalNotes { get; private set; }

    public int? Odometer { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? CommerciallyClosedAtUtc { get; private set; }

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
        RepairOrderPriority priority = RepairOrderPriority.Normal,
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
        Priority = priority;
        OpenedAtUtc = openedAtUtc;
        CustomerConcern = string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();
        InternalNotes = string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();
        Odometer = odometer;
    }

    public void ChangePriority(RepairOrderPriority priority)
    {
        if (Status is RepairOrderStatus.Completed or RepairOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Terminal repair orders cannot change priority.");
        }

        Priority = priority;
    }

    public void UpdateIntake(string? customerConcern, string? internalNotes, int? odometer)
    {
        CustomerConcern = string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();
        InternalNotes = string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();
        Odometer = odometer;
    }

    public void StartWork()
    {
        if (Status != RepairOrderStatus.Draft)
        {
            throw new InvalidOperationException("Only draft repair orders can start work.");
        }

        Status = RepairOrderStatus.InProgress;
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (Status != RepairOrderStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress repair orders can be completed.");
        }

        Status = RepairOrderStatus.Completed;
        CompletedAtUtc = completedAtUtc;
    }

    public void Cancel()
    {
        if (Status is RepairOrderStatus.Completed or RepairOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Terminal repair orders cannot be cancelled.");
        }

        Status = RepairOrderStatus.Cancelled;
    }

    public void CloseCommercially(DateTimeOffset closedAtUtc)
    {
        if (CommerciallyClosedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Repair order is already commercially closed.");
        }

        if (Status != RepairOrderStatus.Completed)
        {
            throw new InvalidOperationException("Only completed repair orders can be commercially closed.");
        }

        CommerciallyClosedAtUtc = closedAtUtc;
    }
}
