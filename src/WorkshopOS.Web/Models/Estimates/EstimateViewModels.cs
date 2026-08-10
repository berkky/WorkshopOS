using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Web.Models.Catalog;

namespace WorkshopOS.Web.Models.Estimates;

public sealed class EstimateListViewModel
{
    public IReadOnlyList<EstimateRowViewModel> Estimates { get; set; } = Array.Empty<EstimateRowViewModel>();

    public string? Search { get; set; }

    public EstimateStatus? Status { get; set; }

    public Guid? WorkshopLocationId { get; set; }

    public Guid? RepairOrderId { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class EstimateRowViewModel
{
    public Guid EstimateId { get; set; }

    public string Number { get; set; } = string.Empty;

    public EstimateStatus Status { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }
}

public sealed class EstimateDetailsViewModel
{
    public Guid EstimateId { get; set; }

    public string Number { get; set; } = string.Empty;

    public EstimateStatus Status { get; set; }

    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public Guid WorkshopLocationId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public string? CustomerMessage { get; set; }

    public IReadOnlyList<EstimateItemRowViewModel> Items { get; set; } =
        Array.Empty<EstimateItemRowViewModel>();

    public IReadOnlyList<InspectionFindingViewModel> InspectionFindings { get; set; } =
        Array.Empty<InspectionFindingViewModel>();

    public Guid? LatestInspectionId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public DateTimeOffset? DeclinedAtUtc { get; set; }

    public bool CanEditItems { get; set; }

    public bool CanPresent { get; set; }

    public bool CanRecordApproval { get; set; }

    public bool CanRecordDecline { get; set; }

    public bool CanManageShares { get; set; }

    public bool CanManageBilling { get; set; }

    public bool CanCreateInvoice { get; set; }

    public Guid? LinkedInvoiceId { get; set; }

    public string? LinkedInvoiceNumber { get; set; }

    public EstimateShareManagementDetails? Share { get; set; }

    public IReadOnlyList<CatalogPickerOptionViewModel> ServiceCatalogOptions { get; set; } =
        Array.Empty<CatalogPickerOptionViewModel>();

    public IReadOnlyList<CatalogPickerOptionViewModel> PartCatalogOptions { get; set; } =
        Array.Empty<CatalogPickerOptionViewModel>();
}

public sealed class EstimateItemRowViewModel
{
    public Guid EstimateItemId { get; set; }

    public EstimateItemType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}

public sealed class InspectionFindingViewModel
{
    public string Section { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public WorkshopOS.Domain.Inspections.InspectionCondition Condition { get; set; }

    public string? Notes { get; set; }
}

public sealed class EstimateItemFormViewModel
{
    public Guid EstimateId { get; set; }

    public Guid? EstimateItemId { get; set; }

    [Display(Name = "Type")]
    public EstimateItemType Type { get; set; } = EstimateItemType.Service;

    [Required]
    [MaxLength(EstimateInputValidator.MaxDescriptionLength)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; } = 1;

    [Required]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }
}

public sealed class EstimateCustomerMessageFormViewModel
{
    public Guid EstimateId { get; set; }

    [MaxLength(EstimateInputValidator.MaxCustomerMessageLength)]
    [Display(Name = "Customer message")]
    public string? CustomerMessage { get; set; }
}
