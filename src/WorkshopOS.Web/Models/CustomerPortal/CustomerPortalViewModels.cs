using WorkshopOS.Domain.Estimates;

namespace WorkshopOS.Web.Models.CustomerPortal;

public sealed class PortalAccessBootstrapViewModel
{
    public Guid PublicId { get; set; }
}

public sealed class CustomerPortalEstimateViewModel
{
    public string OrganizationDisplayName { get; set; } = string.Empty;

    public string EstimateNumber { get; set; } = string.Empty;

    public EstimateStatus Status { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public string VehicleMake { get; set; } = string.Empty;

    public string VehicleModel { get; set; } = string.Empty;

    public int? VehicleModelYear { get; set; }

    public string? VehicleRegistrationPlate { get; set; }

    public string? CustomerMessage { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public DateTimeOffset? PresentedAtUtc { get; set; }

    public DateTimeOffset? DecisionAtUtc { get; set; }

    public EstimateShareDecision? PortalDecision { get; set; }

    public bool CanApprove { get; set; }

    public bool CanDecline { get; set; }

    public IReadOnlyList<CustomerPortalEstimateItemViewModel> Items { get; set; } =
        Array.Empty<CustomerPortalEstimateItemViewModel>();
}

public sealed class CustomerPortalEstimateItemViewModel
{
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}

public sealed class EstimateShareCreatedViewModel
{
    public Guid EstimateId { get; set; }

    public Guid PublicId { get; set; }

    public string RawToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool IsRotation { get; set; }
}

public sealed class EstimateShareSectionViewModel
{
    public Guid EstimateId { get; set; }

    public WorkshopOS.Application.EstimateSharing.EstimateShareManagementDetails? Share { get; set; }
}
