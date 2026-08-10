using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.RepairOrders;

public class RepairOrderTechnicianAssignment : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid RepairOrderId { get; private set; }

    public Guid StaffMemberId { get; private set; }

    public TechnicianWorkStatus WorkStatus { get; private set; }

    public DateTimeOffset AssignedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? WorkCompletedAtUtc { get; private set; }

    public DateTimeOffset? UnassignedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected RepairOrderTechnicianAssignment()
    {
    }

    public RepairOrderTechnicianAssignment(
        Guid organizationId,
        Guid repairOrderId,
        Guid staffMemberId,
        DateTimeOffset assignedAtUtc)
        : base(organizationId)
    {
        if (repairOrderId == Guid.Empty)
        {
            throw new ArgumentException("Repair order identifier is required.", nameof(repairOrderId));
        }

        if (staffMemberId == Guid.Empty)
        {
            throw new ArgumentException("Staff member identifier is required.", nameof(staffMemberId));
        }

        RepairOrderId = repairOrderId;
        StaffMemberId = staffMemberId;
        WorkStatus = TechnicianWorkStatus.Assigned;
        AssignedAtUtc = assignedAtUtc;
    }

    public bool IsActive => UnassignedAtUtc is null;

    public void StartWork(DateTimeOffset startedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Inactive assignments cannot start work.");
        }

        if (WorkStatus != TechnicianWorkStatus.Assigned)
        {
            throw new InvalidOperationException("Only assigned technician work can be started.");
        }

        WorkStatus = TechnicianWorkStatus.InProgress;
        StartedAtUtc = startedAtUtc;
    }

    public void CompleteWork(DateTimeOffset completedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Inactive assignments cannot complete work.");
        }

        if (WorkStatus != TechnicianWorkStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress technician work can be completed.");
        }

        WorkStatus = TechnicianWorkStatus.WorkCompleted;
        WorkCompletedAtUtc = completedAtUtc;
    }

    public void Unassign(DateTimeOffset unassignedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Assignment is already inactive.");
        }

        UnassignedAtUtc = unassignedAtUtc;
    }
}
