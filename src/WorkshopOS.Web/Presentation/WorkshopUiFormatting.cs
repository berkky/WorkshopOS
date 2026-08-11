using System.Globalization;
using Microsoft.Extensions.Localization;
using WorkshopOS.Application.Reporting;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web;

namespace WorkshopOS.Web.Presentation;

public interface IWorkshopUiFormatting
{
    string FormatReportingPeriod(ReportingContextHeader header);

    string FormatLocationScope(string? workshopLocationName);

    string FormatDate(DateOnly date);

    string FormatDate(DateTimeOffset value, string? timeZoneId = null);

    string FormatDateTime(DateTimeOffset value, string? timeZoneId = null);

    string FormatTime(DateTimeOffset value, string? timeZoneId = null);

    string FormatGeneratedAt(DateTimeOffset generatedAtUtc, string timeZoneId);

    string FormatDateRange(DateOnly from, DateOnly to);

    string FormatMembershipRole(OrganizationMembershipRole role);

    string FormatMembershipStatus(OrganizationMembershipStatus status);
}

public sealed class WorkshopUiFormatting(
    IStringLocalizer<SharedResource> localizer,
    IOrganizationContext? organizationContext = null) : IWorkshopUiFormatting
{
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;
    private readonly IOrganizationContext? _organizationContext = organizationContext;

    public string FormatReportingPeriod(ReportingContextHeader header) =>
        FormatDateRange(header.From, header.To);

    public string FormatLocationScope(string? workshopLocationName) =>
        string.IsNullOrWhiteSpace(workshopLocationName)
            ? _localizer["Common_AllLocations"].Value
            : workshopLocationName;

    public string FormatDate(DateOnly date)
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? date.ToString("d MMM yyyy", culture)
            : date.ToString("MMM d, yyyy", culture);
    }

    public string FormatDate(DateTimeOffset value, string? timeZoneId = null) =>
        FormatDate(DateOnly.FromDateTime(ConvertForDisplay(value, ResolveTimeZoneId(timeZoneId)).DateTime));

    public string FormatDateTime(DateTimeOffset value, string? timeZoneId = null)
    {
        var culture = CultureInfo.CurrentUICulture;
        var displayValue = ConvertForDisplay(value, ResolveTimeZoneId(timeZoneId));
        return culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? displayValue.ToString("d MMM yyyy '·' HH:mm", culture)
            : displayValue.ToString("MMM d, yyyy '·' h:mm tt", culture);
    }

    public string FormatTime(DateTimeOffset value, string? timeZoneId = null)
    {
        var culture = CultureInfo.CurrentUICulture;
        var displayValue = ConvertForDisplay(value, ResolveTimeZoneId(timeZoneId));
        return culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? displayValue.ToString("HH:mm", culture)
            : displayValue.ToString("h:mm tt", culture);
    }

    public string FormatGeneratedAt(DateTimeOffset generatedAtUtc, string timeZoneId) =>
        FormatDateTime(generatedAtUtc, timeZoneId);

    public string FormatDateRange(DateOnly from, DateOnly to) =>
        $"{FormatDate(from)} – {FormatDate(to)}";

    public string FormatMembershipRole(OrganizationMembershipRole role) =>
        _localizer[$"Role_{role}"].Value;

    public string FormatMembershipStatus(OrganizationMembershipStatus status) =>
        _localizer[$"MembershipStatus_{status}"].Value;

    private string? ResolveTimeZoneId(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) ? _organizationContext?.TimeZoneId : timeZoneId;

    private static DateTimeOffset ConvertForDisplay(DateTimeOffset value, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return value;
        }

        try
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (TimeZoneNotFoundException)
        {
            return value.ToUniversalTime();
        }
        catch (InvalidTimeZoneException)
        {
            return value.ToUniversalTime();
        }
    }
}
