namespace WorkshopOS.Infrastructure.Tenancy;

public interface IOrganizationContext
{
    Guid? OrganizationId { get; }

    bool IsResolved { get; }
}
