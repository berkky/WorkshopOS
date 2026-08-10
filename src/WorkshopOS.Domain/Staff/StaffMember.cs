using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Staff;

public class StaffMember : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid? UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public StaffPosition Position { get; private set; }

    public string? JobTitle { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? PhoneNumber { get; private set; }

    public StaffStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected StaffMember()
    {
    }

    public StaffMember(
        Guid organizationId,
        string displayName,
        StaffPosition position,
        string? jobTitle = null,
        string? contactEmail = null,
        string? phoneNumber = null,
        Guid? userId = null,
        StaffStatus status = StaffStatus.Active)
        : base(organizationId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Staff display name is required.", nameof(displayName));
        }

        DisplayName = displayName.Trim();
        Position = position;
        JobTitle = NormalizeOptional(jobTitle);
        ContactEmail = NormalizeOptional(contactEmail);
        PhoneNumber = NormalizeOptional(phoneNumber);
        UserId = userId == Guid.Empty ? null : userId;
        Status = status;
    }

    public void UpdateProfile(
        string displayName,
        StaffPosition position,
        string? jobTitle,
        string? contactEmail,
        string? phoneNumber,
        Guid? linkedUserId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Staff display name is required.", nameof(displayName));
        }

        DisplayName = displayName.Trim();
        Position = position;
        JobTitle = NormalizeOptional(jobTitle);
        ContactEmail = NormalizeOptional(contactEmail);
        PhoneNumber = NormalizeOptional(phoneNumber);
        UserId = linkedUserId == Guid.Empty ? null : linkedUserId;
    }

    public void Deactivate() => Status = StaffStatus.Inactive;

    public void Activate() => Status = StaffStatus.Active;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
