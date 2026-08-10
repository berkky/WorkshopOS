using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Appointments;

public class Appointment : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid WorkshopLocationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid VehicleId { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset ScheduledStartUtc { get; private set; }

    public DateTimeOffset ScheduledEndUtc { get; private set; }

    public string? CustomerConcern { get; private set; }

    public string? InternalNotes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Appointment()
    {
    }

    public Appointment(
        Guid organizationId,
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        DateTimeOffset scheduledStartUtc,
        DateTimeOffset scheduledEndUtc,
        AppointmentStatus status = AppointmentStatus.Scheduled,
        string? customerConcern = null,
        string? internalNotes = null)
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

        if (scheduledEndUtc < scheduledStartUtc)
        {
            throw new ArgumentException("Scheduled end must be on or after scheduled start.");
        }

        WorkshopLocationId = workshopLocationId;
        CustomerId = customerId;
        VehicleId = vehicleId;
        ScheduledStartUtc = scheduledStartUtc;
        ScheduledEndUtc = scheduledEndUtc;
        Status = status;
        CustomerConcern = string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();
        InternalNotes = string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();
    }

    public void UpdateBooking(
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        string? customerConcern,
        string? internalNotes)
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

        WorkshopLocationId = workshopLocationId;
        CustomerId = customerId;
        VehicleId = vehicleId;
        CustomerConcern = string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();
        InternalNotes = string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();
    }

    public void Reschedule(
        Guid workshopLocationId,
        DateTimeOffset scheduledStartUtc,
        DateTimeOffset scheduledEndUtc)
    {
        if (workshopLocationId == Guid.Empty)
        {
            throw new ArgumentException("Workshop location identifier is required.", nameof(workshopLocationId));
        }

        if (scheduledEndUtc < scheduledStartUtc)
        {
            throw new ArgumentException("Scheduled end must be on or after scheduled start.");
        }

        WorkshopLocationId = workshopLocationId;
        ScheduledStartUtc = scheduledStartUtc;
        ScheduledEndUtc = scheduledEndUtc;
    }

    public void Cancel() => Status = AppointmentStatus.Cancelled;
}
