namespace WorkshopOS.Application.RepairOrders;

public static class RepairOrderInputValidator
{
    public const int MaxNumberLength = 50;
    public const int MaxCustomerConcernLength = 2000;
    public const int MaxInternalNotesLength = 4000;
    public const int MaxSearchLength = 100;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MinOdometer = 0;
    public const int MaxOdometer = 2_000_000;

    public static string? NormalizeCustomerConcern(string? customerConcern) =>
        string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();

    public static string? NormalizeInternalNotes(string? internalNotes) =>
        string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();

    public static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var trimmed = search.Trim();
        return trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed;
    }

    public static bool IsValidOdometer(int? odometer) =>
        !odometer.HasValue || odometer.Value is >= MinOdometer and <= MaxOdometer;
}
