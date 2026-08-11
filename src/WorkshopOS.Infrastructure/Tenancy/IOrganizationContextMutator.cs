namespace WorkshopOS.Infrastructure.Tenancy;

public interface IOrganizationContextMutator
{
    void Resolve(Guid organizationId);

    void Resolve(Guid organizationId, string? timeZoneId) => Resolve(organizationId);
}
