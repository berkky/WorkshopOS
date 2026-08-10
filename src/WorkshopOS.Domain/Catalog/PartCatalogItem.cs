using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Catalog;

public class PartCatalogItem : OrganizationOwnedEntity, IHasTimestamps
{
    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal DefaultUnitPrice { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected PartCatalogItem()
    {
    }

    public PartCatalogItem(
        Guid organizationId,
        string sku,
        string name,
        string? description,
        decimal defaultUnitPrice,
        string currencyCode,
        bool isActive = true)
        : base(organizationId)
    {
        ValidateSku(sku);
        ValidateName(name);
        ValidatePrice(defaultUnitPrice);
        ValidateCurrencyCode(currencyCode);

        Sku = NormalizeSku(sku);
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultUnitPrice = defaultUnitPrice;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        IsActive = isActive;
    }

    public void Update(
        string sku,
        string name,
        string? description,
        decimal defaultUnitPrice,
        bool isActive)
    {
        ValidateSku(sku);
        ValidateName(name);
        ValidatePrice(defaultUnitPrice);

        Sku = NormalizeSku(sku);
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultUnitPrice = defaultUnitPrice;
        IsActive = isActive;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public string BuildEstimateDescription() =>
        string.IsNullOrWhiteSpace(Description) ? $"{Name} ({Sku})" : $"{Name} ({Sku}) — {Description}";

    public static string NormalizeSku(string sku) =>
        sku.Trim().ToUpperInvariant();

    private static void ValidateSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Part SKU is required.", nameof(sku));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Part name is required.", nameof(name));
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
