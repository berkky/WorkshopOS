using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Vehicles;

public class Vehicle : OrganizationOwnedEntity
{
    public Guid? CurrentCustomerId { get; private set; }

    public string? Vin { get; private set; }

    public string? RegistrationPlate { get; private set; }

    public string Make { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    public int? ModelYear { get; private set; }

    public string? Color { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Vehicle()
    {
    }

    public Vehicle(
        Guid organizationId,
        string make,
        string model,
        Guid? currentCustomerId = null,
        string? vin = null,
        string? registrationPlate = null,
        int? modelYear = null,
        string? color = null)
        : base(organizationId)
    {
        if (string.IsNullOrWhiteSpace(make))
        {
            throw new ArgumentException("Vehicle make is required.", nameof(make));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Vehicle model is required.", nameof(model));
        }

        Make = make.Trim();
        Model = model.Trim();
        CurrentCustomerId = currentCustomerId;
        Vin = string.IsNullOrWhiteSpace(vin) ? null : vin.Trim();
        RegistrationPlate = string.IsNullOrWhiteSpace(registrationPlate) ? null : registrationPlate.Trim();
        ModelYear = modelYear;
        Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
    }
}
