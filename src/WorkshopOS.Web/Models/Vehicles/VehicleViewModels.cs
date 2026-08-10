using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.Vehicles;

namespace WorkshopOS.Web.Models.Vehicles;

public sealed class VehicleListViewModel
{
    public IReadOnlyList<VehicleRowViewModel> Vehicles { get; set; } =
        Array.Empty<VehicleRowViewModel>();

    public string? Search { get; set; }

    public Guid? CurrentCustomerId { get; set; }

    public string? CurrentCustomerDisplayName { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanManageVehicles { get; set; }
}

public sealed class VehicleRowViewModel
{
    public Guid VehicleId { get; set; }

    public string Make { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int? ModelYear { get; set; }

    public string? Vin { get; set; }

    public string? RegistrationPlate { get; set; }

    public string? CurrentCustomerDisplayName { get; set; }
}

public sealed class VehicleFormViewModel
{
    public Guid? VehicleId { get; set; }

    [Required]
    [MaxLength(VehicleInputValidator.MaxMakeLength)]
    [Display(Name = "Make")]
    public string Make { get; set; } = string.Empty;

    [Required]
    [MaxLength(VehicleInputValidator.MaxModelLength)]
    [Display(Name = "Model")]
    public string Model { get; set; } = string.Empty;

    [Display(Name = "Model year")]
    public int? ModelYear { get; set; }

    [MaxLength(VehicleInputValidator.MaxVinLength)]
    [Display(Name = "VIN / chassis")]
    public string? Vin { get; set; }

    [MaxLength(VehicleInputValidator.MaxRegistrationPlateLength)]
    [Display(Name = "License plate")]
    public string? RegistrationPlate { get; set; }

    [MaxLength(VehicleInputValidator.MaxColorLength)]
    [Display(Name = "Color")]
    public string? Color { get; set; }

    [Display(Name = "Current customer")]
    public Guid? CurrentCustomerId { get; set; }

    public IReadOnlyList<CustomerOptionViewModel> CustomerOptions { get; set; } =
        Array.Empty<CustomerOptionViewModel>();
}

public sealed class VehicleReassignViewModel
{
    public Guid VehicleId { get; set; }

    public string VehicleSummary { get; set; } = string.Empty;

    public string? CurrentCustomerDisplayName { get; set; }

    [Display(Name = "New customer")]
    public Guid? NewCurrentCustomerId { get; set; }

    public IReadOnlyList<CustomerOptionViewModel> CustomerOptions { get; set; } =
        Array.Empty<CustomerOptionViewModel>();
}

public sealed class VehicleDetailsViewModel
{
    public Guid VehicleId { get; set; }

    public string Make { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int? ModelYear { get; set; }

    public string? Vin { get; set; }

    public string? RegistrationPlate { get; set; }

    public string? Color { get; set; }

    public Guid? CurrentCustomerId { get; set; }

    public string? CurrentCustomerDisplayName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public int RepairOrderCount { get; set; }

    public DateTimeOffset? LastRepairOrderOpenedAtUtc { get; set; }

    public Guid? LastRepairOrderId { get; set; }

    public string? LastRepairOrderNumber { get; set; }

    public WorkshopOS.Domain.RepairOrders.RepairOrderStatus? LastRepairOrderStatus { get; set; }

    public int UpcomingAppointmentCount { get; set; }

    public DateTimeOffset? NextAppointmentStartUtc { get; set; }

    public bool CanManageVehicles { get; set; }
}

public sealed class CustomerOptionViewModel
{
    public Guid CustomerId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}
