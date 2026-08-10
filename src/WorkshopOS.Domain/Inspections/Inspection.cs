using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Inspections;

public class Inspection : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid RepairOrderId { get; private set; }

    public InspectionStatus Status { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? TechnicianNotes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Inspection()
    {
    }

    public Inspection(
        Guid organizationId,
        Guid repairOrderId,
        InspectionStatus status = InspectionStatus.Draft,
        string? technicianNotes = null)
        : base(organizationId)
    {
        if (repairOrderId == Guid.Empty)
        {
            throw new ArgumentException("Repair order identifier is required.", nameof(repairOrderId));
        }

        RepairOrderId = repairOrderId;
        Status = status;
        TechnicianNotes = string.IsNullOrWhiteSpace(technicianNotes) ? null : technicianNotes.Trim();
    }

    public void Start(DateTimeOffset startedAtUtc)
    {
        if (Status is InspectionStatus.Completed or InspectionStatus.Cancelled)
        {
            throw new InvalidOperationException("Terminal inspections cannot be started.");
        }

        if (Status == InspectionStatus.InProgress)
        {
            return;
        }

        if (Status != InspectionStatus.Draft)
        {
            throw new InvalidOperationException("Only draft inspections can be started.");
        }

        Status = InspectionStatus.InProgress;
        StartedAtUtc = startedAtUtc;
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (Status == InspectionStatus.Completed)
        {
            return;
        }

        if (Status != InspectionStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress inspections can be completed.");
        }

        Status = InspectionStatus.Completed;
        CompletedAtUtc = completedAtUtc;
    }
}
