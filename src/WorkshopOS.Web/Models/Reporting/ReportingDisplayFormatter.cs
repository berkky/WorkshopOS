using System.Globalization;
using WorkshopOS.Application.Reporting;

namespace WorkshopOS.Web.Models.Reporting;

public static class ReportingDisplayFormatter
{
    public static string FormatPeriodLabel(ReportingContextHeader header) =>
        $"{header.From:MMM d, yyyy} – {header.To:MMM d, yyyy}";

    public static string FormatLocationLabel(ReportingContextHeader header) =>
        header.WorkshopLocationName ?? "All locations";

    public static string FormatMoney(MoneyBreakdownItem item) =>
        $"{item.CurrencyCode} {item.Amount.ToString("N2", CultureInfo.InvariantCulture)}";

    public static string FormatApprovalRate(decimal? rate) =>
        rate.HasValue ? $"{rate.Value.ToString("0.0", CultureInfo.InvariantCulture)}%" : "—";
}
