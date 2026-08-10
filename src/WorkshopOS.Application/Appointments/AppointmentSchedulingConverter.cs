namespace WorkshopOS.Application.Appointments;

public static class AppointmentSchedulingConverter
{
    public static bool TryConvertLocalToUtc(
        string timeZoneId,
        DateTime localDateTime,
        out DateTimeOffset utc,
        out string? error)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            if (timeZone.IsInvalidTime(localDateTime))
            {
                utc = default;
                error = "The selected local time does not exist due to daylight-saving adjustment.";
                return false;
            }

            if (timeZone.IsAmbiguousTime(localDateTime))
            {
                var offsets = timeZone.GetAmbiguousTimeOffsets(localDateTime);
                utc = new DateTimeOffset(localDateTime, offsets[0]).ToUniversalTime();
                error = null;
                return true;
            }

            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
            utc = new DateTimeOffset(utcDateTime, TimeSpan.Zero);
            error = null;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            utc = default;
            error = "The configured time zone is not available.";
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            utc = default;
            error = "The configured time zone is not available.";
            return false;
        }
    }

    public static DateTimeOffset ConvertUtcToLocal(DateTimeOffset utc, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }

    public static DateOnly GetWeekStartMonday(DateOnly referenceDate)
    {
        var dayOfWeek = referenceDate.DayOfWeek;
        var daysFromMonday = ((int)dayOfWeek + 6) % 7;
        return referenceDate.AddDays(-daysFromMonday);
    }
}
