using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Staff;

public class StaffLocationAssignment : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid StaffMemberId { get; private set; }

    public Guid WorkshopLocationId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected StaffLocationAssignment()
    {
    }

    public StaffLocationAssignment(
        Guid organizationId,
        Guid staffMemberId,
        Guid workshopLocationId)
        : base(organizationId)
    {
        if (staffMemberId == Guid.Empty)
        {
            throw new ArgumentException("Staff member identifier is required.", nameof(staffMemberId));
        }

        if (workshopLocationId == Guid.Empty)
        {
            throw new ArgumentException("Workshop location identifier is required.", nameof(workshopLocationId));
        }

        StaffMemberId = staffMemberId;
        WorkshopLocationId = workshopLocationId;
    }
}
