using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.Customers;
using WorkshopOS.Web.Models.RepairOrders;
using WorkshopOS.Web;

namespace WorkshopOS.Web.Models.Customers;

public sealed class CustomerListViewModel
{
    public IReadOnlyList<CustomerRowViewModel> Customers { get; set; } =
        Array.Empty<CustomerRowViewModel>();

    public string? Search { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanManageCustomers { get; set; }
}

public sealed class CustomerRowViewModel
{
    public Guid CustomerId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; }
}

public sealed class CustomerFormViewModel
{
    public Guid? CustomerId { get; set; }

    [Required]
    [MaxLength(CustomerInputValidator.MaxDisplayNameLength)]
    [Display(Name = "Field_DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(CustomerInputValidator.MaxEmailLength)]
    [Display(Name = "Field_Email")]
    public string? Email { get; set; }

    [MaxLength(CustomerInputValidator.MaxPhoneLength)]
    [Display(Name = "Field_Phone")]
    public string? Phone { get; set; }

    [MaxLength(CustomerInputValidator.MaxNotesLength)]
    [Display(Name = "Field_Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Field_Active")]
    public bool IsActive { get; set; } = true;
}

public sealed class CustomerDetailsViewModel
{
    public Guid CustomerId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public int VehicleCount { get; set; }

    public int UpcomingAppointmentCount { get; set; }

    public int RepairOrderCount { get; set; }

    public IReadOnlyList<CustomerRepairOrderSummaryViewModel> RecentRepairOrders { get; set; } =
        Array.Empty<CustomerRepairOrderSummaryViewModel>();

    public bool CanManageCustomers { get; set; }
}
