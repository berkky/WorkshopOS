namespace WorkshopOS.Application.Catalog;

public static class CatalogManagerPolicy
{
    public static bool CanManageCatalog(Domain.Organizations.OrganizationMembershipRole role) =>
        role is Domain.Organizations.OrganizationMembershipRole.Owner
            or Domain.Organizations.OrganizationMembershipRole.Administrator
            or Domain.Organizations.OrganizationMembershipRole.ServiceAdvisor;
}

public static class CatalogInputValidator
{
    public const int MaxCodeLength = 64;

    public const int MaxSkuLength = 64;

    public const int MaxNameLength = 200;

    public const int MaxDescriptionLength = 2000;

    public static bool TryNormalizeCode(string? code, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        normalized = Domain.Catalog.ServiceCatalogItem.NormalizeCode(code);
        return normalized.Length <= MaxCodeLength;
    }

    public static bool TryNormalizeSku(string? sku, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        normalized = Domain.Catalog.PartCatalogItem.NormalizeSku(sku);
        return normalized.Length <= MaxSkuLength;
    }

    public static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        normalized = name.Trim();
        return normalized.Length <= MaxNameLength;
    }

    public static bool TryNormalizeDescription(string? description, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            normalized = null;
            return true;
        }

        normalized = description.Trim();
        return normalized.Length <= MaxDescriptionLength;
    }

    public static bool IsValidPrice(decimal price) => price >= 0;

    public static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
