using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Organizations;

public class OrganizationMembership : Entity, IHasTimestamps
{
    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public OrganizationMembershipRole Role { get; private set; }

    public OrganizationMembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected OrganizationMembership()
    {
    }

    public OrganizationMembership(
        Guid organizationId,
        Guid userId,
        OrganizationMembershipRole role,
        OrganizationMembershipStatus status = OrganizationMembershipStatus.Active)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization identifier is required.", nameof(organizationId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier is required.", nameof(userId));
        }

        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        Status = status;
    }

    public void ChangeRole(OrganizationMembershipRole role) => Role = role;

    public void ChangeStatus(OrganizationMembershipStatus status) => Status = status;
}
