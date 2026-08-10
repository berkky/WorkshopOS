using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Application.Team;
using WorkshopOS.Application.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Web.Models.Appointments;

namespace WorkshopOS.Web.Models.RepairOrders;

public sealed class RepairOrderListViewModel
{
    public IReadOnlyList<RepairOrderRowViewModel> RepairOrders { get; set; } =
        Array.Empty<RepairOrderRowViewModel>();

    public string? Search { get; set; }

    public RepairOrderStatus? Status { get; set; }

    public Guid? WorkshopLocationId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? VehicleId { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanManageRepairOrders { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class RepairOrderRowViewModel
{
    public Guid RepairOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public RepairOrderStatus Status { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public DateTimeOffset OpenedAtUtc { get; set; }
}

public sealed class RepairOrderFormViewModel
{
    public Guid? RepairOrderId { get; set; }

    [Required]
    [Display(Name = "Workshop location")]
    public Guid WorkshopLocationId { get; set; }

    [Required]
    [Display(Name = "Customer")]
    public Guid CustomerId { get; set; }

    [Required]
    [Display(Name = "Vehicle")]
    public Guid VehicleId { get; set; }

    [MaxLength(RepairOrderInputValidator.MaxCustomerConcernLength)]
    [Display(Name = "Customer concern")]
    public string? CustomerConcern { get; set; }

    [MaxLength(RepairOrderInputValidator.MaxInternalNotesLength)]
    [Display(Name = "Internal notes")]
    public string? InternalNotes { get; set; }

    [Range(RepairOrderInputValidator.MinOdometer, RepairOrderInputValidator.MaxOdometer)]
    [Display(Name = "Odometer")]
    public int? Odometer { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();

    public IReadOnlyList<CustomerOptionViewModel> CustomerOptions { get; set; } =
        Array.Empty<CustomerOptionViewModel>();

    public IReadOnlyList<VehicleOptionViewModel> VehicleOptions { get; set; } =
        Array.Empty<VehicleOptionViewModel>();
}

public sealed class RepairOrderDetailsViewModel
{
    public Guid RepairOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public RepairOrderStatus Status { get; set; }

    public Guid WorkshopLocationId { get; set; }

    public string WorkshopLocationName { get; set; } = string.Empty;

    public Guid? AppointmentId { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }

    public string VehicleSummary { get; set; } = string.Empty;

    public string? CustomerConcern { get; set; }

    public string? InternalNotes { get; set; }

    public int? Odometer { get; set; }

    public DateTimeOffset OpenedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public bool CanManageRepairOrders { get; set; }

    public bool CanStartWork { get; set; }

    public bool CanComplete { get; set; }

    public bool CanCancel { get; set; }

    public bool CanEditIntake { get; set; }

    public RepairOrderPriority Priority { get; set; }

    public string? AssignedTechnicianDisplayName { get; set; }

    public bool AssignedTechnicianIsInactive { get; set; }

    public TechnicianWorkStatus? TechnicianWorkStatus { get; set; }

    public bool CanManageOperations { get; set; }

    public bool CanAssignTechnician { get; set; }

    public bool CanReassignTechnician { get; set; }

    public bool CanUnassignTechnician { get; set; }

    public bool CanChangePriority { get; set; }

    public bool CanStartOwnWork { get; set; }

    public bool CanCompleteOwnWork { get; set; }

    public IReadOnlyList<EligibleTechnicianOptionViewModel> EligibleTechnicians { get; set; } =
        Array.Empty<EligibleTechnicianOptionViewModel>();

    public IReadOnlyList<AssignmentHistoryViewModel> AssignmentHistory { get; set; } =
        Array.Empty<AssignmentHistoryViewModel>();

    public int InspectionCount { get; set; }

    public Guid? LatestInspectionId { get; set; }

    public InspectionStatus? LatestInspectionStatus { get; set; }

    public DateTimeOffset? LatestInspectionCreatedAtUtc { get; set; }

    public DateTimeOffset? LatestInspectionCompletedAtUtc { get; set; }

    public bool CanCreateInspection { get; set; }

    public IReadOnlyList<RepairOrderInspectionRowViewModel> Inspections { get; set; } =
        Array.Empty<RepairOrderInspectionRowViewModel>();

    public int EstimateCount { get; set; }

    public Guid? LatestEstimateId { get; set; }

    public EstimateStatus? LatestEstimateStatus { get; set; }

    public decimal? LatestEstimateTotal { get; set; }

    public string? LatestEstimateCurrencyCode { get; set; }

    public bool CanCreateEstimate { get; set; }

    public bool CanManageEstimates { get; set; }

    public IReadOnlyList<RepairOrderEstimateRowViewModel> Estimates { get; set; } =
        Array.Empty<RepairOrderEstimateRowViewModel>();

    public Guid? CurrentInvoiceId { get; set; }

    public string? CurrentInvoiceNumber { get; set; }

    public WorkshopOS.Domain.Billing.InvoiceStatus? CurrentInvoiceStatus { get; set; }

    public InvoicePaymentState? InvoicePaymentState { get; set; }

    public decimal? InvoiceTotal { get; set; }

    public decimal? InvoiceAmountPaid { get; set; }

    public decimal? InvoiceRemainingBalance { get; set; }

    public bool CommerciallyClosed { get; set; }

    public bool CanCreateInvoice { get; set; }

    public bool CanCloseCommercially { get; set; }

    public bool CanManageBilling { get; set; }

    public IReadOnlyList<RepairOrderInvoiceRowViewModel> Invoices { get; set; } =
        Array.Empty<RepairOrderInvoiceRowViewModel>();
}

public sealed class RepairOrderEstimateRowViewModel
{
    public Guid EstimateId { get; set; }

    public string Number { get; set; } = string.Empty;

    public EstimateStatus Status { get; set; }

    public decimal Total { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }
}

public sealed class RepairOrderInvoiceRowViewModel
{
    public Guid InvoiceId { get; set; }

    public string Number { get; set; } = string.Empty;

    public WorkshopOS.Domain.Billing.InvoiceStatus Status { get; set; }

    public InvoicePaymentState PaymentState { get; set; }

    public decimal Total { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? IssuedAtUtc { get; set; }
}

public sealed class RepairOrderInspectionRowViewModel
{
    public Guid InspectionId { get; set; }

    public InspectionStatus Status { get; set; }

    public int TotalItems { get; set; }

    public int InspectedItems { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class AssignmentHistoryViewModel
{
    public string TechnicianDisplayName { get; set; } = string.Empty;

    public TechnicianWorkStatus WorkStatus { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; }

    public DateTimeOffset? UnassignedAtUtc { get; set; }
}

public sealed class EligibleTechnicianOptionViewModel
{
    public Guid StaffMemberId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CustomerRepairOrderSummaryViewModel
{
    public Guid RepairOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public RepairOrderStatus Status { get; set; }

    public string VehicleSummary { get; set; } = string.Empty;

    public DateTimeOffset OpenedAtUtc { get; set; }
}
