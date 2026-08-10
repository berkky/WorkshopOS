using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Customers;

public class Customer : OrganizationOwnedEntity, IHasTimestamps
{
    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Customer()
    {
    }

    public Customer(
        Guid organizationId,
        string displayName,
        string? email = null,
        string? phone = null,
        string? notes = null,
        bool isActive = true)
        : base(organizationId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Customer display name is required.", nameof(displayName));
        }

        DisplayName = displayName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        IsActive = isActive;
    }

    public void UpdateProfile(
        string displayName,
        string? email,
        string? phone,
        string? notes,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Customer display name is required.", nameof(displayName));
        }

        DisplayName = displayName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        IsActive = isActive;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
