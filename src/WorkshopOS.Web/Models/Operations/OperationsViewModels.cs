using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Web.Models.Operations;

public sealed class OperationsBoardViewModel
{
    public OperationsBoardSummaryViewModel Summary { get; set; } = new();

    public IReadOnlyList<OperationsBoardItemViewModel> IntakeItems { get; set; } =
        Array.Empty<OperationsBoardItemViewModel>();

    public IReadOnlyList<OperationsBoardItemViewModel> InProgressItems { get; set; } =
        Array.Empty<OperationsBoardItemViewModel>();

    public IReadOnlyList<OperationsBoardItemViewModel> OtherActiveItems { get; set; } =
        Array.Empty<OperationsBoardItemViewModel>();

    public Guid? WorkshopLocationId { get; set; }

    public RepairOrderStatus? Status { get; set; }

    public RepairOrderPriority? Priority { get; set; }

    public Guid? AssignedStaffMemberId { get; set; }

    public bool UnassignedOnly { get; set; }

    public string? Search { get; set; }

    public int TotalMatchingCount { get; set; }

    public bool ResultsTruncated { get; set; }

    public bool CanManageOperations { get; set; }

    public IReadOnlyList<WorkshopOS.Application.Team.WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopOS.Application.Team.WorkshopLocationOption>();

    public IReadOnlyList<TechnicianFilterOptionViewModel> TechnicianOptions { get; set; } =
        Array.Empty<TechnicianFilterOptionViewModel>();
}

public sealed class OperationsBoardSummaryViewModel
{
    public int ActiveJobs { get; set; }

    public int Unassigned { get; set; }

    public int InProgress { get; set; }

    public int Urgent { get; set; }
}

public sealed class OperationsBoardItemViewModel
{
    public Guid RepairOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public RepairOrderStatus Status { get; set; }

    public RepairOrderPriority Priority { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public DateTimeOffset OpenedAtUtc { get; set; }

    public string? AssignedTechnicianDisplayName { get; set; }

    public bool AssignedTechnicianIsInactive { get; set; }

    public TechnicianWorkStatus? TechnicianWorkStatus { get; set; }

    public InspectionStatus? LatestInspectionStatus { get; set; }

    public EstimateStatus? LatestEstimateStatus { get; set; }
}

public sealed class TechnicianFilterOptionViewModel
{
    public Guid StaffMemberId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AssignTechnicianFormViewModel
{
    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public Guid StaffMemberId { get; set; }

    public IReadOnlyList<EligibleTechnicianOptionViewModel> EligibleTechnicians { get; set; } =
        Array.Empty<EligibleTechnicianOptionViewModel>();
}

public sealed class ReassignTechnicianFormViewModel
{
    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public string? CurrentTechnicianDisplayName { get; set; }

    public Guid NewStaffMemberId { get; set; }

    public IReadOnlyList<EligibleTechnicianOptionViewModel> EligibleTechnicians { get; set; } =
        Array.Empty<EligibleTechnicianOptionViewModel>();
}

public sealed class ChangePriorityFormViewModel
{
    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public RepairOrderPriority Priority { get; set; }
}

public sealed class EligibleTechnicianOptionViewModel
{
    public Guid StaffMemberId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}
