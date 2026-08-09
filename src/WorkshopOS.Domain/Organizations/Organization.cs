using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Organizations;

public class Organization : Entity
{
    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public OrganizationStatus Status { get; private set; }

    public string DefaultCurrencyCode { get; private set; } = string.Empty;

    public string TimeZoneId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Organization()
    {
    }

    public Organization(
        string name,
        string slug,
        string defaultCurrencyCode,
        string timeZoneId,
        OrganizationStatus status = OrganizationStatus.Active)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Organization slug is required.", nameof(slug));
        }

        if (string.IsNullOrWhiteSpace(defaultCurrencyCode))
        {
            throw new ArgumentException("Default currency code is required.", nameof(defaultCurrencyCode));
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Time zone identifier is required.", nameof(timeZoneId));
        }

        Name = name.Trim();
        Slug = slug.Trim();
        DefaultCurrencyCode = defaultCurrencyCode.Trim().ToUpperInvariant();
        TimeZoneId = timeZoneId.Trim();
        Status = status;
    }
}
