using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Appointments;

namespace WorkshopOS.Web.Models.Appointments;

public sealed class AppointmentListViewModel
{
    public IReadOnlyList<AppointmentRowViewModel> Appointments { get; set; } =
        Array.Empty<AppointmentRowViewModel>();

    public Guid? WorkshopLocationId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? VehicleId { get; set; }

    public AppointmentStatus? Status { get; set; }

    public DateTimeOffset? FromUtc { get; set; }

    public DateTimeOffset? ToUtc { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanManageAppointments { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class AppointmentRowViewModel
{
    public Guid AppointmentId { get; set; }

    public DateTimeOffset ScheduledStartUtc { get; set; }

    public DateTimeOffset ScheduledEndUtc { get; set; }

    public AppointmentStatus Status { get; set; }

    public string WorkshopLocationName { get; set; } = string.Empty;

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = "UTC";

    public DateTimeOffset ScheduledStartLocal { get; set; }

    public DateTimeOffset ScheduledEndLocal { get; set; }
}

public sealed class AppointmentCalendarViewModel
{
    public DateOnly WeekStartLocal { get; set; }

    public DateOnly WeekEndLocal { get; set; }

    public string TimeZoneId { get; set; } = "UTC";

    public Guid? WorkshopLocationId { get; set; }

    public AppointmentStatus? Status { get; set; }

    public bool CanManageAppointments { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();

    public IReadOnlyList<AppointmentCalendarDayViewModel> Days { get; set; } =
        Array.Empty<AppointmentCalendarDayViewModel>();
}

public sealed class AppointmentCalendarDayViewModel
{
    public DateOnly Date { get; set; }

    public IReadOnlyList<AppointmentCalendarEntryViewModel> Appointments { get; set; } =
        Array.Empty<AppointmentCalendarEntryViewModel>();
}

public sealed class AppointmentCalendarEntryViewModel
{
    public Guid AppointmentId { get; set; }

    public DateTimeOffset ScheduledStartLocal { get; set; }

    public DateTimeOffset ScheduledEndLocal { get; set; }

    public AppointmentStatus Status { get; set; }

    public string WorkshopLocationName { get; set; } = string.Empty;

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;
}

public sealed class AppointmentFormViewModel
{
    public Guid? AppointmentId { get; set; }

    [Required]
    [Display(Name = "Workshop location")]
    public Guid WorkshopLocationId { get; set; }

    [Required]
    [Display(Name = "Customer")]
    public Guid CustomerId { get; set; }

    [Required]
    [Display(Name = "Vehicle")]
    public Guid VehicleId { get; set; }

    [Required]
    [Display(Name = "Date")]
    [DataType(DataType.Date)]
    public DateOnly LocalDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    [Display(Name = "Start time")]
    public TimeOnly StartTime { get; set; } = new(9, 0);

    [Required]
    [Display(Name = "End time")]
    public TimeOnly EndTime { get; set; } = new(10, 0);

    [MaxLength(AppointmentInputValidator.MaxCustomerConcernLength)]
    [Display(Name = "Customer concern")]
    public string? CustomerConcern { get; set; }

    [MaxLength(AppointmentInputValidator.MaxInternalNotesLength)]
    [Display(Name = "Internal notes")]
    public string? InternalNotes { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();

    public IReadOnlyList<CustomerOptionViewModel> CustomerOptions { get; set; } =
        Array.Empty<CustomerOptionViewModel>();

    public IReadOnlyList<VehicleOptionViewModel> VehicleOptions { get; set; } =
        Array.Empty<VehicleOptionViewModel>();
}

public sealed class AppointmentRescheduleViewModel
{
    public Guid AppointmentId { get; set; }

    public string Summary { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Workshop location")]
    public Guid WorkshopLocationId { get; set; }

    [Required]
    [Display(Name = "Date")]
    [DataType(DataType.Date)]
    public DateOnly LocalDate { get; set; }

    [Required]
    [Display(Name = "Start time")]
    public TimeOnly StartTime { get; set; }

    [Required]
    [Display(Name = "End time")]
    public TimeOnly EndTime { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class AppointmentDetailsViewModel
{
    public Guid AppointmentId { get; set; }

    public string WorkshopLocationName { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }

    public string VehicleSummary { get; set; } = string.Empty;

    public AppointmentStatus Status { get; set; }

    public DateTimeOffset ScheduledStartLocal { get; set; }

    public DateTimeOffset ScheduledEndLocal { get; set; }

    public string TimeZoneId { get; set; } = "UTC";

    public string? CustomerConcern { get; set; }

    public string? InternalNotes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public bool CanManageAppointments { get; set; }

    public Guid? LinkedRepairOrderId { get; set; }

    public bool CanManageRepairOrders { get; set; }
}

public sealed class CustomerOptionViewModel
{
    public Guid CustomerId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class VehicleOptionViewModel
{
    public Guid VehicleId { get; set; }

    public string Summary { get; set; } = string.Empty;
}
