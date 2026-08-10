using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Inventory;

namespace WorkshopOS.Web.Models.Inventory;

public sealed class InventoryListViewModel
{
    public IReadOnlyList<InventoryRowViewModel> Items { get; set; } = Array.Empty<InventoryRowViewModel>();

    public Guid? WorkshopLocationId { get; set; }

    public Guid? PartCatalogItemId { get; set; }

    public bool LowOrZeroStockOnly { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public bool CanAdjustInventory { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class InventoryRowViewModel
{
    public Guid PartCatalogItemId { get; set; }

    public string PartSku { get; set; } = string.Empty;

    public string PartName { get; set; } = string.Empty;

    public bool PartIsActive { get; set; }

    public Guid WorkshopLocationId { get; set; }

    public string WorkshopLocationName { get; set; } = string.Empty;

    public decimal QuantityOnHand { get; set; }
}

public sealed class InventoryAdjustFormViewModel
{
    [Required]
    [Display(Name = "Part")]
    public Guid PartCatalogItemId { get; set; }

    [Required]
    [Display(Name = "Location")]
    public Guid WorkshopLocationId { get; set; }

    [Required]
    [Display(Name = "Movement type")]
    public PartInventoryMovementType MovementType { get; set; } = PartInventoryMovementType.ManualIncrease;

    [Required]
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; } = 1;

    [MaxLength(InventoryInputValidator.MaxReasonLength)]
    [Display(Name = "Reason (optional)")]
    public string? Reason { get; set; }
}

public sealed class InventoryHistoryViewModel
{
    public Guid PartCatalogItemId { get; set; }

    public Guid WorkshopLocationId { get; set; }

    public string PartSku { get; set; } = string.Empty;

    public string PartName { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public IReadOnlyList<InventoryMovementRowViewModel> Movements { get; set; } =
        Array.Empty<InventoryMovementRowViewModel>();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }
}

public sealed class InventoryMovementRowViewModel
{
    public Guid MovementId { get; set; }

    public PartInventoryMovementType MovementType { get; set; }

    public decimal QuantityDelta { get; set; }

    public decimal BalanceAfter { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}
