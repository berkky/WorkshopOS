namespace WorkshopOS.Application.Customers;

public static class CustomerInputValidator
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxEmailLength = 320;
    public const int MaxPhoneLength = 50;
    public const int MaxNotesLength = 4000;
    public const int MaxSearchLength = 100;

    public static string NormalizeDisplayName(string displayName) => displayName.Trim();

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    public static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    public static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var trimmed = search.Trim();
        return trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed;
    }

    public static bool IsValidEmailShape(string? email)
    {
        if (email is null)
        {
            return true;
        }

        return email.Contains('@', StringComparison.Ordinal)
               && email.IndexOf('@') > 0
               && email.IndexOf('@') < email.Length - 1;
    }
}
