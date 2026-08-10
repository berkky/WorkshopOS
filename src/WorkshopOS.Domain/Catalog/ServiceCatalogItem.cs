using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Catalog;

public class ServiceCatalogItem : OrganizationOwnedEntity, IHasTimestamps
{
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal DefaultUnitPrice { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected ServiceCatalogItem()
    {
    }

    public ServiceCatalogItem(
        Guid organizationId,
        string code,
        string name,
        string? description,
        decimal defaultUnitPrice,
        string currencyCode,
        bool isActive = true)
        : base(organizationId)
    {
        ValidateCode(code);
        ValidateName(name);
        ValidatePrice(defaultUnitPrice);
        ValidateCurrencyCode(currencyCode);

        Code = NormalizeCode(code);
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultUnitPrice = defaultUnitPrice;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        IsActive = isActive;
    }

    public void Update(
        string code,
        string name,
        string? description,
        decimal defaultUnitPrice,
        bool isActive)
    {
        ValidateCode(code);
        ValidateName(name);
        ValidatePrice(defaultUnitPrice);

        Code = NormalizeCode(code);
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultUnitPrice = defaultUnitPrice;
        IsActive = isActive;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public string BuildEstimateDescription() =>
        string.IsNullOrWhiteSpace(Description) ? Name : $"{Name} — {Description}";

    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Service code is required.", nameof(code));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Service name is required.", nameof(name));
        }
    }

    private static void ValidatePrice(decimal defaultUnitPrice)
    {
        if (defaultUnitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultUnitPrice), "Default unit price cannot be negative.");
        }
    }

    private static void ValidateCurrencyCode(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
        {
            throw new ArgumentException("Currency code must be a 3-character ISO code.", nameof(currencyCode));
        }
    }
}
