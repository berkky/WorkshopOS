using WorkshopOS.Domain.Inspections;

namespace WorkshopOS.Web.Models.Inspections;

public sealed class InspectionListViewModel
{
    public IReadOnlyList<InspectionRowViewModel> Inspections { get; set; } =
        Array.Empty<InspectionRowViewModel>();

    public string? Search { get; set; }

    public InspectionStatus? Status { get; set; }

    public Guid? WorkshopLocationId { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public IReadOnlyList<WorkshopOS.Application.Team.WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopOS.Application.Team.WorkshopLocationOption>();
}

public sealed class InspectionRowViewModel
{
    public Guid InspectionId { get; set; }

    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public InspectionStatus Status { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public int TotalItems { get; set; }

    public int InspectedItems { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class InspectionDetailsViewModel
{
    public Guid InspectionId { get; set; }

    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public InspectionStatus Status { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public IReadOnlyList<InspectionSectionViewModel> Sections { get; set; } =
        Array.Empty<InspectionSectionViewModel>();

    public InspectionSummaryViewModel Summary { get; set; } = new();

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public bool CanStart { get; set; }

    public bool CanUpdateItems { get; set; }

    public bool CanComplete { get; set; }

    public bool CanUploadMedia { get; set; }

    public bool CanRemoveMedia { get; set; }

    public int ActivePhotoCount { get; set; }

    public List<InspectionItemFormViewModel> EditableItems { get; set; } = new();
}

public sealed class InspectionSectionViewModel
{
    public string Section { get; set; } = string.Empty;

    public IReadOnlyList<InspectionItemDisplayViewModel> Items { get; set; } =
        Array.Empty<InspectionItemDisplayViewModel>();
}

public sealed class InspectionItemDisplayViewModel
{
    public Guid InspectionItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public InspectionCondition Condition { get; set; }

    public string? Notes { get; set; }

    public int SortOrder { get; set; }

    public IReadOnlyList<InspectionMediaDisplayViewModel> Media { get; set; } =
        Array.Empty<InspectionMediaDisplayViewModel>();
}

public sealed class InspectionMediaDisplayViewModel
{
    public Guid MediaId { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long LengthBytes { get; set; }

    public string? Caption { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; }
}

public sealed class InspectionItemFormViewModel
{
    public Guid InspectionItemId { get; set; }

    public InspectionCondition Condition { get; set; }

    public string? Notes { get; set; }
}

public sealed class InspectionSummaryViewModel
{
    public int Total { get; set; }

    public int Inspected { get; set; }

    public int Good { get; set; }

    public int Attention { get; set; }

    public int Critical { get; set; }

    public int Monitor { get; set; }
}
