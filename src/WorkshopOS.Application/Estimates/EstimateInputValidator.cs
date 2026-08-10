namespace WorkshopOS.Application.Estimates;

public static class EstimateInputValidator
{
    public const int MaxDescriptionLength = 500;

    public const int MaxCustomerMessageLength = 2000;

    public const int MaxItemsPerEstimate = 100;

    public const decimal MaxQuantity = 999999999.999m;

    public const decimal MaxUnitPrice = 9999999999999999.99m;

    public static bool TryNormalizeDescription(string? description, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = description.Trim();
        return normalized.Length <= MaxDescriptionLength;
    }

    public static bool TryNormalizeCustomerMessage(string? customerMessage, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(customerMessage))
        {
            normalized = null;
            return true;
        }

        normalized = customerMessage.Trim();
        return normalized.Length <= MaxCustomerMessageLength;
    }

    public static bool IsValidQuantity(decimal quantity) =>
        quantity > 0 && quantity <= MaxQuantity;

    public static bool IsValidUnitPrice(decimal unitPrice) =>
        unitPrice >= 0 && unitPrice <= MaxUnitPrice;
}
