namespace WorkshopOS.Application.Appointments;

public static class AppointmentInputValidator
{
    public const int MaxCustomerConcernLength = 2000;
    public const int MaxInternalNotesLength = 4000;
    public const int MaxAppointmentDurationHours = 24;
    public const int MaxCalendarRangeDays = 92;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static string? NormalizeCustomerConcern(string? customerConcern) =>
        string.IsNullOrWhiteSpace(customerConcern) ? null : customerConcern.Trim();

    public static string? NormalizeInternalNotes(string? internalNotes) =>
        string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();

    public static bool IsValidTimeRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc <= startUtc)
        {
            return false;
        }

        return (endUtc - startUtc).TotalHours <= MaxAppointmentDurationHours;
    }

    public static bool IsWithinCalendarRange(DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc) =>
        rangeEndUtc > rangeStartUtc
        && (rangeEndUtc - rangeStartUtc).TotalDays <= MaxCalendarRangeDays;
}
