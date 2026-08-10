using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkshopOS.Application.Onboarding;

public static class OnboardingInputValidator
{
    private static readonly Regex CurrencyCodePattern = new("^[A-Za-z]{3}$", RegexOptions.CultureInvariant);

    public static bool TryValidateTimeZoneId(string timeZoneId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            error = "Time zone is required.";
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            error = "Time zone is not valid.";
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            error = "Time zone is not valid.";
            return false;
        }
    }

    public static bool TryValidateCurrencyCode(string currencyCode, out string normalized, out string? error)
    {
        normalized = currencyCode.Trim().ToUpperInvariant();
        error = null;

        if (!CurrencyCodePattern.IsMatch(normalized))
        {
            error = "Currency code must be exactly three letters.";
            return false;
        }

        return true;
    }

    public static string NormalizeLocationCode(string locationCode) =>
        locationCode.Trim().ToUpperInvariant();

    public static string NormalizeOrganizationName(string organizationName) =>
        organizationName.Trim();

    public static string NormalizeWorkshopLocationName(string locationName) =>
        locationName.Trim();

    public static string NormalizeOwnerDisplayName(string displayName) =>
        displayName.Trim();

    public static string NormalizeEmail(string email) =>
        email.Trim();
}
