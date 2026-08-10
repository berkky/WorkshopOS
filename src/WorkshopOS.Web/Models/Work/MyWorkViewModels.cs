using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Web.Models.Work;

public sealed class MyWorkViewModel
{
    public bool HasLinkedTechnicianProfile { get; set; }

    public IReadOnlyList<MyWorkItemViewModel> Items { get; set; } = Array.Empty<MyWorkItemViewModel>();
}

public sealed class MyWorkItemViewModel
{
    public Guid RepairOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public RepairOrderPriority Priority { get; set; }

    public RepairOrderStatus RepairOrderStatus { get; set; }

    public TechnicianWorkStatus TechnicianWorkStatus { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public bool CanStartWork { get; set; }

    public bool CanCompleteWork { get; set; }

    public Guid? LatestInspectionId { get; set; }

    public InspectionStatus? LatestInspectionStatus { get; set; }

    public EstimateStatus? LatestEstimateStatus { get; set; }
}
