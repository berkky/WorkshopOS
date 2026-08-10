using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Application.Team;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Inventory;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("inventory")]
public sealed class InventoryController : Controller
{
    private readonly IInventoryManagementService _inventoryManagementService;
    private readonly IPartCatalogService _partCatalogService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IAuthorizationService _authorizationService;

    public InventoryController(
        IInventoryManagementService inventoryManagementService,
        IPartCatalogService partCatalogService,
        ITeamManagementService teamManagementService,
        IAuthorizationService authorizationService)
    {
        _inventoryManagementService = inventoryManagementService;
        _partCatalogService = partCatalogService;
        _teamManagementService = teamManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid? workshopLocationId,
        Guid? partCatalogItemId,
        bool lowOrZeroStockOnly = false,
        int page = 1,
        int pageSize = InventoryListQuery.DefaultPageSize)
    {
        var result = await _inventoryManagementService.ListInventoryAsync(new InventoryListQuery
        {
            WorkshopLocationId = workshopLocationId,
            PartCatalogItemId = partCatalogItemId,
            LowOrZeroStockOnly = lowOrZeroStockOnly,
            Page = page,
            PageSize = pageSize,
        });

        var canAdjust = (await _authorizationService.AuthorizeAsync(User, PolicyNames.InventoryManager)).Succeeded;
        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new InventoryListViewModel
        {
            WorkshopLocationId = workshopLocationId,
            PartCatalogItemId = partCatalogItemId,
            LowOrZeroStockOnly = lowOrZeroStockOnly,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanAdjustInventory = canAdjust,
            LocationOptions = locationOptions,
            Items = result.Items
                .Select(item => new InventoryRowViewModel
                {
                    PartCatalogItemId = item.PartCatalogItemId,
                    PartSku = item.PartSku,
                    PartName = item.PartName,
                    PartIsActive = item.PartIsActive,
                    WorkshopLocationId = item.WorkshopLocationId,
                    WorkshopLocationName = item.WorkshopLocationName,
                    QuantityOnHand = item.QuantityOnHand,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.InventoryManager)]
    [HttpGet("adjust")]
    public async Task<IActionResult> Adjust(Guid? partCatalogItemId, Guid? workshopLocationId)
    {
        ViewBag.PartOptions = await LoadActivePartOptionsAsync();
        ViewBag.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new InventoryAdjustFormViewModel
        {
            PartCatalogItemId = partCatalogItemId ?? Guid.Empty,
            WorkshopLocationId = workshopLocationId ?? Guid.Empty,
        });
    }

    [Authorize(Policy = PolicyNames.InventoryManager)]
    [HttpPost("adjust")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(InventoryAdjustFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.PartOptions = await LoadActivePartOptionsAsync();
            ViewBag.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
            return View(model);
        }

        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _inventoryManagementService.AdjustInventoryAsync(
            actorUserId.Value,
            new AdjustInventoryCommand
            {
                PartCatalogItemId = model.PartCatalogItemId,
                WorkshopLocationId = model.WorkshopLocationId,
                MovementType = model.MovementType,
                Quantity = model.Quantity,
                Reason = model.Reason,
            });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            ViewBag.PartOptions = await LoadActivePartOptionsAsync();
            ViewBag.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
            return View(model);
        }

        return RedirectToAction(
            nameof(History),
            new
            {
                partCatalogItemId = model.PartCatalogItemId,
                workshopLocationId = model.WorkshopLocationId,
            });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(
        Guid partCatalogItemId,
        Guid workshopLocationId,
        int page = 1,
        int pageSize = InventoryMovementHistoryQuery.DefaultPageSize)
    {
        var part = await _partCatalogService.GetPartDetailsAsync(partCatalogItemId);
        if (part is null)
        {
            return NotFound();
        }

        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
        var locationName = locationOptions
            .SingleOrDefault(option => option.WorkshopLocationId == workshopLocationId)?.Name ?? "Location";

        var result = await _inventoryManagementService.GetMovementHistoryAsync(new InventoryMovementHistoryQuery
        {
            PartCatalogItemId = partCatalogItemId,
            WorkshopLocationId = workshopLocationId,
            Page = page,
            PageSize = pageSize,
        });

        return View(new InventoryHistoryViewModel
        {
            PartCatalogItemId = partCatalogItemId,
            WorkshopLocationId = workshopLocationId,
            PartSku = part.Sku,
            PartName = part.Name,
            WorkshopLocationName = locationName,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Movements = result.Items
                .Select(movement => new InventoryMovementRowViewModel
                {
                    MovementId = movement.MovementId,
                    MovementType = movement.MovementType,
                    QuantityDelta = movement.QuantityDelta,
                    BalanceAfter = movement.BalanceAfter,
                    Reason = movement.Reason,
                    OccurredAtUtc = movement.OccurredAtUtc,
                })
                .ToList(),
        });
    }

    private async Task<IReadOnlyList<PartCatalogListItem>> LoadActivePartOptionsAsync()
    {
        var result = await _partCatalogService.ListPartsAsync(new PartCatalogListQuery
        {
            IsActive = true,
            PageSize = PartCatalogListQuery.MaxPageSize,
        });

        return result.Items;
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string MapFailure(InventoryAdjustmentFailureReason? reason) =>
        reason switch
        {
            InventoryAdjustmentFailureReason.InsufficientStock =>
                "This adjustment would result in negative inventory.",
            InventoryAdjustmentFailureReason.PartInactive => "Inactive parts cannot be adjusted.",
            InventoryAdjustmentFailureReason.LocationInactive => "Inactive locations cannot receive adjustments.",
            InventoryAdjustmentFailureReason.InvalidInput => "One or more adjustment fields are invalid.",
            InventoryAdjustmentFailureReason.Unauthorized => "You are not authorized to adjust inventory.",
            InventoryAdjustmentFailureReason.ConcurrencyConflict =>
                "Inventory was updated by another operation. Refresh and try again.",
            _ => "The inventory adjustment could not be completed.",
        };
}
