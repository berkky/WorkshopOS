using System.Globalization;
using WorkshopOS.Application.Reporting;

namespace WorkshopOS.Web.Models.Reporting;

public static class ReportingDisplayFormatter
{
    public static string FormatPeriodLabel(ReportingContextHeader header)
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? $"{header.From.ToString("dd MMM yyyy", culture)} – {header.To.ToString("dd MMM yyyy", culture)}"
            : $"{header.From.ToString("MMM d, yyyy", culture)} – {header.To.ToString("MMM d, yyyy", culture)}";
    }

    public static string FormatLocationLabel(ReportingContextHeader header) =>
        header.WorkshopLocationName ?? string.Empty;

    public static string FormatMoney(MoneyBreakdownItem item) =>
        $"{item.CurrencyCode} {item.Amount.ToString("N2", CultureInfo.InvariantCulture)}";

    public static string FormatApprovalRate(decimal? rate) =>
        rate.HasValue ? $"{rate.Value.ToString("0.0", CultureInfo.InvariantCulture)}%" : "—";
}
