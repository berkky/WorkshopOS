using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Organizations;

public class WorkshopLocation : OrganizationOwnedEntity
{
    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? TimeZoneId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected WorkshopLocation()
    {
    }

    public WorkshopLocation(
        Guid organizationId,
        string name,
        string code,
        string? timeZoneId = null,
        bool isActive = true)
        : base(organizationId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Workshop location name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Workshop location code is required.", nameof(code));
        }

        Name = name.Trim();
        Code = code.Trim();
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        IsActive = isActive;
    }
}
