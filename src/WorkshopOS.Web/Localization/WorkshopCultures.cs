using System.Globalization;

namespace WorkshopOS.Web.Localization;

public static class WorkshopCultures
{
    public const string English = "en-US";
    public const string Turkish = "tr-TR";

    public static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(English);
    public static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo(Turkish);

    public static readonly IReadOnlySet<string> SupportedCultureNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { English, Turkish };

    public static bool IsSupported(string? cultureName) =>
        !string.IsNullOrWhiteSpace(cultureName) && SupportedCultureNames.Contains(cultureName);

    public static string NormalizeOrDefault(string? cultureName) =>
        IsSupported(cultureName) ? cultureName! : English;

    public static string ToHtmlLang(string cultureName) =>
        cultureName.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
}
