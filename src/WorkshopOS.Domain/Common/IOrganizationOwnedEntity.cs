namespace WorkshopOS.Domain.Common;

public interface IOrganizationOwnedEntity
{
    Guid OrganizationId { get; }
}
