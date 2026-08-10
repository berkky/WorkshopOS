namespace WorkshopOS.Application.Vehicles;

public static class VehicleInputValidator
{
    public const int MaxMakeLength = 100;
    public const int MaxModelLength = 100;
    public const int MaxVinLength = 32;
    public const int MaxRegistrationPlateLength = 32;
    public const int MaxColorLength = 60;
    public const int MaxSearchLength = 100;
    public const int MinModelYear = 1886;

    public static string NormalizeMake(string make) => make.Trim();

    public static string NormalizeModel(string model) => model.Trim();

    public static string? NormalizeVin(string? vin)
    {
        if (string.IsNullOrWhiteSpace(vin))
        {
            return null;
        }

        return vin.Trim().ToUpperInvariant();
    }

    public static string? NormalizeRegistrationPlate(string? registrationPlate)
    {
        if (string.IsNullOrWhiteSpace(registrationPlate))
        {
            return null;
        }

        return registrationPlate.Trim().ToUpperInvariant();
    }

    public static string? NormalizeColor(string? color) =>
        string.IsNullOrWhiteSpace(color) ? null : color.Trim();

    public static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var trimmed = search.Trim();
        return trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed;
    }

    public static bool IsValidModelYear(int? modelYear, int currentYear)
    {
        if (!modelYear.HasValue)
        {
            return true;
        }

        return modelYear.Value >= MinModelYear && modelYear.Value <= currentYear + 2;
    }
}
