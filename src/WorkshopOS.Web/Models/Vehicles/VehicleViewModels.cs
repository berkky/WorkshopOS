using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Web;

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
    [Display(Name = "Field_Make")]
    public string Make { get; set; } = string.Empty;

    [Required]
    [MaxLength(VehicleInputValidator.MaxModelLength)]
    [Display(Name = "Field_Model")]
    public string Model { get; set; } = string.Empty;

    [Display(Name = "Field_ModelYear")]
    public int? ModelYear { get; set; }

    [MaxLength(VehicleInputValidator.MaxVinLength)]
    [Display(Name = "Field_VinChassis")]
    public string? Vin { get; set; }

    [MaxLength(VehicleInputValidator.MaxRegistrationPlateLength)]
    [Display(Name = "Field_LicensePlate")]
    public string? RegistrationPlate { get; set; }

    [MaxLength(VehicleInputValidator.MaxColorLength)]
    [Display(Name = "Field_Color")]
    public string? Color { get; set; }

    [Display(Name = "Field_CurrentCustomer")]
    public Guid? CurrentCustomerId { get; set; }

    public IReadOnlyList<CustomerOptionViewModel> CustomerOptions { get; set; } =
        Array.Empty<CustomerOptionViewModel>();
}

public sealed class VehicleReassignViewModel
{
    public Guid VehicleId { get; set; }

    public string VehicleSummary { get; set; } = string.Empty;

    public string? CurrentCustomerDisplayName { get; set; }

    [Display(Name = "Field_NewCustomer")]
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
