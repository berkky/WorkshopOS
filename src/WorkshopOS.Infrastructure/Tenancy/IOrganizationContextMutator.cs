namespace WorkshopOS.Infrastructure.Tenancy;

public interface IOrganizationContextMutator
{
    void Resolve(Guid organizationId);
}
