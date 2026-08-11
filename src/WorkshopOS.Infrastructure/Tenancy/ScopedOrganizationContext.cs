namespace WorkshopOS.Infrastructure.Tenancy;

public sealed class ScopedOrganizationContext : IOrganizationContext, IOrganizationContextMutator
{
    public Guid? OrganizationId { get; private set; }

    public string? TimeZoneId { get; private set; }

    public bool IsResolved => OrganizationId.HasValue;

    public void Resolve(Guid organizationId) => Resolve(organizationId, null);

    public void Resolve(Guid organizationId, string? timeZoneId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization identifier cannot be empty.", nameof(organizationId));
        }

        OrganizationId = organizationId;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
    }
}
