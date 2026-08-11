namespace WorkshopOS.Infrastructure.Tenancy;

public interface IOrganizationContext
{
    Guid? OrganizationId { get; }

    string? TimeZoneId => null;

    bool IsResolved { get; }
}
