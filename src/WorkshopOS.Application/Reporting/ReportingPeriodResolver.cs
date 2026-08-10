namespace WorkshopOS.Application.Reporting;

public static class ReportingPeriodResolver
{
    public const int DefaultInclusiveDayCount = 30;

    public const int MaxInclusiveDayCount = 366;

    public static DateOnly ResolveTodayInTimeZone(DateTimeOffset utcNow, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static ReportingPeriod CreateDefaultPeriod(DateOnly todayInBusinessTimeZone) =>
        new(todayInBusinessTimeZone.AddDays(-(DefaultInclusiveDayCount - 1)), todayInBusinessTimeZone);

    public static bool TryResolvePeriod(
        DateOnly? from,
        DateOnly? to,
        DateOnly todayInBusinessTimeZone,
        out ReportingPeriod period,
        out ReportingFailureReason? failureReason)
    {
        period = default!;
        failureReason = null;

        if (from is null && to is null)
        {
            period = CreateDefaultPeriod(todayInBusinessTimeZone);
            return true;
        }

        if (from is null || to is null)
        {
            failureReason = ReportingFailureReason.InvalidDateRange;
            return false;
        }

        if (from > to)
        {
            failureReason = ReportingFailureReason.InvalidDateRange;
            return false;
        }

        var resolvedFrom = from.Value;
        var resolvedTo = to.Value;

        if (resolvedTo.DayNumber - resolvedFrom.DayNumber > MaxInclusiveDayCount - 1)
        {
            failureReason = ReportingFailureReason.InvalidDateRange;
            return false;
        }

        period = new ReportingPeriod(resolvedFrom, resolvedTo);
        return true;
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtcExclusive) ConvertToUtcBounds(
        ReportingPeriod period,
        string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var startLocal = period.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocalExclusive = period.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        var startUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone),
            TimeSpan.Zero);
        var endUtcExclusive = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(endLocalExclusive, timeZone),
            TimeSpan.Zero);

        return (startUtc, endUtcExclusive);
    }
}

public readonly record struct ReportingPeriod(DateOnly From, DateOnly To);
