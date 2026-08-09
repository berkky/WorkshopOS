namespace WorkshopOS.Domain.Common;

public abstract class OrganizationOwnedEntity : Entity, IOrganizationOwnedEntity
{
    public Guid OrganizationId { get; protected set; }

    protected OrganizationOwnedEntity()
    {
    }

    protected OrganizationOwnedEntity(Guid organizationId)
    {
        SetOrganizationId(organizationId);
    }

    protected OrganizationOwnedEntity(Guid organizationId, Guid id)
        : base(id)
    {
        SetOrganizationId(organizationId);
    }

    private void SetOrganizationId(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization identifier cannot be empty.", nameof(organizationId));
        }

        OrganizationId = organizationId;
    }
}
