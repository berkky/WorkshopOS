using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal sealed class TestOrganizationContext : IOrganizationContext
{
    public TestOrganizationContext(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization identifier cannot be empty.", nameof(organizationId));
        }

        OrganizationId = organizationId;
        IsResolved = true;
    }

    public Guid? OrganizationId { get; }

    public bool IsResolved { get; }
}
