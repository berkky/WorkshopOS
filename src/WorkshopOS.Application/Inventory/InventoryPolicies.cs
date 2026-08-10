namespace WorkshopOS.Application.Inventory;

public static class InventoryManagerPolicy
{
    public static bool CanManageInventory(Domain.Organizations.OrganizationMembershipRole role) =>
        role is Domain.Organizations.OrganizationMembershipRole.Owner
            or Domain.Organizations.OrganizationMembershipRole.Administrator;
}

public static class InventoryInputValidator
{
    public const int MaxReasonLength = 500;

    public static bool TryNormalizeReason(string? reason, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            normalized = null;
            return true;
        }

        normalized = reason.Trim();
        return normalized.Length <= MaxReasonLength;
    }

    public static bool IsValidQuantity(decimal quantity) => quantity > 0;
}
