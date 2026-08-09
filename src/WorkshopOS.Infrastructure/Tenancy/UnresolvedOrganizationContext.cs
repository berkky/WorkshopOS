namespace WorkshopOS.Infrastructure.Tenancy;

public sealed class UnresolvedOrganizationContext : IOrganizationContext
{
    public Guid? OrganizationId => null;

    public bool IsResolved => false;
}
