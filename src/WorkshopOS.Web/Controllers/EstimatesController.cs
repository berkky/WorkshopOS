using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.CustomerPortal;
using WorkshopOS.Web.Models.Catalog;
using WorkshopOS.Web.Models.Estimates;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
public sealed class EstimatesController : Controller
{
    private readonly IEstimateManagementService _estimateManagementService;
    private readonly IInvoiceManagementService _invoiceManagementService;
    private readonly IEstimateSharingService _estimateSharingService;
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly IPartCatalogService _partCatalogService;
    private readonly IInventoryManagementService _inventoryManagementService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IAuthorizationService _authorizationService;

    public EstimatesController(
        IEstimateManagementService estimateManagementService,
        IInvoiceManagementService invoiceManagementService,
        IEstimateSharingService estimateSharingService,
        IServiceCatalogService serviceCatalogService,
        IPartCatalogService partCatalogService,
        IInventoryManagementService inventoryManagementService,
        ITeamManagementService teamManagementService,
        IAuthorizationService authorizationService)
    {
        _estimateManagementService = estimateManagementService;
        _invoiceManagementService = invoiceManagementService;
        _estimateSharingService = estimateSharingService;
        _serviceCatalogService = serviceCatalogService;
        _partCatalogService = partCatalogService;
        _inventoryManagementService = inventoryManagementService;
        _teamManagementService = teamManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("/estimates")]
    public async Task<IActionResult> Index(
        string? search,
        EstimateStatus? status,
        Guid? workshopLocationId,
        Guid? repairOrderId,
        int page = 1,
        int pageSize = EstimateListQuery.DefaultPageSize)
    {
        var result = await _estimateManagementService.ListEstimatesAsync(new EstimateListQuery
        {
            Search = search,
            Status = status,
            WorkshopLocationId = workshopLocationId,
            RepairOrderId = repairOrderId,
            Page = page,
            PageSize = pageSize,
        });

        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new EstimateListViewModel
        {
            Search = search,
            Status = status,
            WorkshopLocationId = workshopLocationId,
            RepairOrderId = repairOrderId,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            LocationOptions = locationOptions,
            Estimates = result.Items
                .Select(estimate => new EstimateRowViewModel
                {
                    EstimateId = estimate.EstimateId,
                    Number = estimate.Number,
                    Status = estimate.Status,
                    CurrencyCode = estimate.CurrencyCode,
                    Total = estimate.Total,
                    RepairOrderId = estimate.RepairOrderId,
                    RepairOrderNumber = estimate.RepairOrderNumber,
                    CustomerDisplayName = estimate.CustomerDisplayName,
                    VehicleSummary = estimate.VehicleSummary,
                    WorkshopLocationName = estimate.WorkshopLocationName,
                    CreatedAtUtc = estimate.CreatedAtUtc,
                    SentAtUtc = estimate.SentAtUtc,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/repair-orders/{repairOrderId:guid}/estimates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.CreateEstimateAsync(actorUserId.Value, repairOrderId);
        if (!result.Success)
        {
            if (result.FailureReason == EstimateOperationFailureReason.RepairOrderNotFound)
            {
                return NotFound();
            }

            if (result.FailureReason == EstimateOperationFailureReason.Unauthorized)
            {
                return Forbid();
            }

            TempData["EstimateError"] = MapFailure(result.FailureReason);
            return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
        }

        return RedirectToAction(nameof(Edit), new { estimateId = result.Value });
    }

    [HttpGet("/estimates/{estimateId:guid}")]
    public async Task<IActionResult> Details(Guid estimateId)
    {
        var details = await _estimateManagementService.GetEstimateDetailsAsync(estimateId);
        if (details is null)
        {
            return NotFound();
        }

        var canManageShares = (await _authorizationService.AuthorizeAsync(User, PolicyNames.EstimateManager)).Succeeded;
        var canManageBilling = (await _authorizationService.AuthorizeAsync(User, PolicyNames.BillingManager)).Succeeded;
        var share = canManageShares
            ? await _estimateSharingService.GetEstimateShareDetailsAsync(estimateId)
            : null;

        Guid? linkedInvoiceId = null;
        string? linkedInvoiceNumber = null;
        if (details.Status == EstimateStatus.Approved)
        {
            var billingSummary = await _invoiceManagementService.GetRepairOrderBillingSummaryAsync(details.RepairOrderId);
            if (billingSummary?.CurrentInvoiceId is Guid currentInvoiceId)
            {
                var linkedInvoice = await _invoiceManagementService.GetInvoiceDetailsAsync(currentInvoiceId);
                if (linkedInvoice?.SourceEstimateId == estimateId)
                {
                    linkedInvoiceId = linkedInvoice.InvoiceId;
                    linkedInvoiceNumber = linkedInvoice.Number;
                }
            }
        }

        var model = MapDetailsViewModel(details);
        model.CanManageShares = canManageShares;
        model.CanManageBilling = canManageBilling;
        model.CanCreateInvoice = canManageBilling
                                 && details.Status == EstimateStatus.Approved
                                 && linkedInvoiceId is null;
        model.LinkedInvoiceId = linkedInvoiceId;
        model.LinkedInvoiceNumber = linkedInvoiceNumber;
        model.Share = share;
        return View(model);
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpGet("/estimates/{estimateId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid estimateId)
    {
        var details = await _estimateManagementService.GetEstimateDetailsAsync(estimateId);
        if (details is null)
        {
            return NotFound();
        }

        if (!details.CanEditItems)
        {
            return RedirectToAction(nameof(Details), new { estimateId });
        }

        var model = MapDetailsViewModel(details);
        await PopulateCatalogOptionsAsync(model, details.WorkshopLocationId);
        return View(model);
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/catalog/service")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddServiceFromCatalog(
        Guid estimateId,
        Guid serviceCatalogItemId,
        decimal quantity = 1)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.AddServiceCatalogItemAsync(
            actorUserId.Value,
            new AddServiceCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                ServiceCatalogItemId = serviceCatalogItemId,
                Quantity = quantity,
            });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/catalog/part")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPartFromCatalog(
        Guid estimateId,
        Guid partCatalogItemId,
        decimal quantity = 1)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.AddPartCatalogItemAsync(
            actorUserId.Value,
            new AddPartCatalogItemToEstimateCommand
            {
                EstimateId = estimateId,
                PartCatalogItemId = partCatalogItemId,
                Quantity = quantity,
            });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(Guid estimateId, EstimateItemFormViewModel model)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return await RedirectToEditOrDetails(estimateId);
        }

        var result = await _estimateManagementService.AddEstimateItemAsync(
            actorUserId.Value,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Type = model.Type,
                Description = model.Description,
                Quantity = model.Quantity,
                UnitPrice = model.UnitPrice,
            });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/items/{itemId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditItem(Guid estimateId, Guid itemId, EstimateItemFormViewModel model)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return await RedirectToEditOrDetails(estimateId);
        }

        var result = await _estimateManagementService.UpdateEstimateItemAsync(
            actorUserId.Value,
            new UpdateEstimateItemCommand
            {
                EstimateId = estimateId,
                EstimateItemId = itemId,
                Type = model.Type,
                Description = model.Description,
                Quantity = model.Quantity,
                UnitPrice = model.UnitPrice,
            });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/items/{itemId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid estimateId, Guid itemId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.RemoveEstimateItemAsync(
            actorUserId.Value,
            estimateId,
            itemId);

        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/customer-message")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCustomerMessage(
        Guid estimateId,
        EstimateCustomerMessageFormViewModel model)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(Edit), new { estimateId });
        }

        var result = await _estimateManagementService.UpdateEstimateCustomerMessageAsync(
            actorUserId.Value,
            estimateId,
            model.CustomerMessage);

        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/present")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Present(Guid estimateId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.PresentForApprovalAsync(actorUserId.Value, estimateId);
        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
            return RedirectToAction(nameof(Edit), new { estimateId });
        }

        return RedirectToAction(nameof(Details), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/record-approval")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordApproval(Guid estimateId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.RecordCustomerApprovalAsync(actorUserId.Value, estimateId);
        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Details), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/record-decline")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordDecline(Guid estimateId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateManagementService.RecordCustomerDeclineAsync(actorUserId.Value, estimateId);
        if (!result.Success)
        {
            TempData["EstimateError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Details), new { estimateId });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/share/create")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CreateShare(Guid estimateId, int durationDays = EstimateShareExpiryPolicy.DefaultDurationDays)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateSharingService.CreateShareAsync(
            actorUserId.Value,
            new CreateEstimateShareCommand { EstimateId = estimateId, DurationDays = durationDays });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapShareFailure(result.FailureReason);
            return RedirectToAction(nameof(Details), new { estimateId });
        }

        Response.Headers.CacheControl = "no-store";
        return View("ShareCreated", new EstimateShareCreatedViewModel
        {
            EstimateId = estimateId,
            PublicId = result.Value!.PublicId,
            RawToken = result.Value.RawToken,
            ExpiresAtUtc = result.Value.ExpiresAtUtc,
            IsRotation = false,
        });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/share/rotate")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RotateShare(Guid estimateId, int durationDays = EstimateShareExpiryPolicy.DefaultDurationDays)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateSharingService.RotateShareAsync(
            actorUserId.Value,
            new RotateEstimateShareCommand { EstimateId = estimateId, DurationDays = durationDays });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapShareFailure(result.FailureReason);
            return RedirectToAction(nameof(Details), new { estimateId });
        }

        Response.Headers.CacheControl = "no-store";
        return View("ShareCreated", new EstimateShareCreatedViewModel
        {
            EstimateId = estimateId,
            PublicId = result.Value!.PublicId,
            RawToken = result.Value.RawToken,
            ExpiresAtUtc = result.Value.ExpiresAtUtc,
            IsRotation = true,
        });
    }

    [Authorize(Policy = PolicyNames.EstimateManager)]
    [HttpPost("/estimates/{estimateId:guid}/share/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeShare(Guid estimateId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _estimateSharingService.RevokeShareAsync(
            actorUserId.Value,
            new RevokeEstimateShareCommand { EstimateId = estimateId });

        if (!result.Success)
        {
            TempData["EstimateError"] = MapShareFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Details), new { estimateId });
    }

    private async Task<IActionResult> RedirectToEditOrDetails(Guid estimateId)
    {
        var details = await _estimateManagementService.GetEstimateDetailsAsync(estimateId);
        if (details?.CanEditItems == true)
        {
            return RedirectToAction(nameof(Edit), new { estimateId });
        }

        return RedirectToAction(nameof(Details), new { estimateId });
    }

    private static EstimateDetailsViewModel MapDetailsViewModel(EstimateDetails details) =>
        new()
        {
            EstimateId = details.EstimateId,
            Number = details.Number,
            Status = details.Status,
            RepairOrderId = details.RepairOrderId,
            RepairOrderNumber = details.RepairOrderNumber,
            CustomerDisplayName = details.CustomerDisplayName,
            VehicleSummary = details.VehicleSummary,
            WorkshopLocationName = details.WorkshopLocationName,
            WorkshopLocationId = details.WorkshopLocationId,
            CurrencyCode = details.CurrencyCode,
            Total = details.Total,
            CustomerMessage = details.CustomerMessage,
            Items = details.Items
                .Select(item => new EstimateItemRowViewModel
                {
                    EstimateItemId = item.EstimateItemId,
                    Type = item.Type,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal,
                    SortOrder = item.SortOrder,
                })
                .ToList(),
            InspectionFindings = details.InspectionFindings
                .Select(finding => new InspectionFindingViewModel
                {
                    Section = finding.Section,
                    Name = finding.Name,
                    Condition = finding.Condition,
                    Notes = finding.Notes,
                })
                .ToList(),
            LatestInspectionId = details.LatestInspectionId,
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            SentAtUtc = details.SentAtUtc,
            ApprovedAtUtc = details.ApprovedAtUtc,
            DeclinedAtUtc = details.DeclinedAtUtc,
            CanEditItems = details.CanEditItems,
            CanPresent = details.CanPresent,
            CanRecordApproval = details.CanRecordApproval,
            CanRecordDecline = details.CanRecordDecline,
        };

    private async Task PopulateCatalogOptionsAsync(EstimateDetailsViewModel model, Guid workshopLocationId)
    {
        var services = await _serviceCatalogService.ListServicesAsync(new ServiceCatalogListQuery
        {
            IsActive = true,
            PageSize = ServiceCatalogListQuery.MaxPageSize,
        });

        model.ServiceCatalogOptions = services.Items
            .Where(item => string.Equals(item.CurrencyCode, model.CurrencyCode, StringComparison.Ordinal))
            .Select(item => new CatalogPickerOptionViewModel
            {
                Id = item.ServiceCatalogItemId,
                Label = $"{item.Code} — {item.Name}",
                DefaultUnitPrice = item.DefaultUnitPrice,
                CurrencyCode = item.CurrencyCode,
            })
            .ToList();

        var parts = await _partCatalogService.ListPartsAsync(new PartCatalogListQuery
        {
            IsActive = true,
            PageSize = PartCatalogListQuery.MaxPageSize,
        });

        var partOptions = new List<CatalogPickerOptionViewModel>();
        foreach (var part in parts.Items.Where(item =>
                     string.Equals(item.CurrencyCode, model.CurrencyCode, StringComparison.Ordinal)))
        {
            var stock = await _inventoryManagementService.GetQuantityOnHandAsync(
                part.PartCatalogItemId,
                workshopLocationId);

            partOptions.Add(new CatalogPickerOptionViewModel
            {
                Id = part.PartCatalogItemId,
                Label = $"{part.Sku} — {part.Name}",
                DefaultUnitPrice = part.DefaultUnitPrice,
                CurrencyCode = part.CurrencyCode,
                StockOnHand = stock,
            });
        }

        model.PartCatalogOptions = partOptions;
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string MapFailure(EstimateOperationFailureReason? reason) =>
        reason switch
        {
            EstimateOperationFailureReason.OrganizationUnresolved => "Organization context is not resolved.",
            EstimateOperationFailureReason.EstimateNotFound => "Estimate was not found.",
            EstimateOperationFailureReason.RepairOrderNotFound => "Repair order was not found.",
            EstimateOperationFailureReason.Unauthorized => "You are not authorized to manage estimates.",
            EstimateOperationFailureReason.InvalidInput => "One or more estimate fields are invalid.",
            EstimateOperationFailureReason.RepairOrderNotEligible =>
                "Estimates cannot be created for completed or cancelled repair orders.",
            EstimateOperationFailureReason.InvalidLifecycleTransition =>
                "This estimate cannot be changed in its current status.",
            EstimateOperationFailureReason.EmptyEstimate =>
                "At least one line item is required before presenting for approval.",
            EstimateOperationFailureReason.ConcurrencyConflict =>
                "The estimate was updated by another operation. Refresh and try again.",
            EstimateOperationFailureReason.ItemNotFound => "Estimate line item was not found.",
            EstimateOperationFailureReason.ItemLimitExceeded =>
                "This estimate has reached the maximum number of line items.",
            EstimateOperationFailureReason.ItemBelongsToAnotherEstimate =>
                "The line item does not belong to this estimate.",
            EstimateOperationFailureReason.CatalogItemNotFound => "The selected catalog item was not found.",
            EstimateOperationFailureReason.CatalogItemInactive =>
                "Inactive catalog items cannot be added to estimates.",
            EstimateOperationFailureReason.CurrencyMismatch =>
                "The catalog item currency does not match this estimate.",
            _ => "The estimate operation could not be completed.",
        };

    private static string MapShareFailure(EstimateShareOperationFailureReason? reason) =>
        reason switch
        {
            EstimateShareOperationFailureReason.EstimateNotEligible =>
                "Only sent, approved, or declined estimates can be shared with customers.",
            EstimateShareOperationFailureReason.ActiveShareAlreadyExists =>
                "A current secure share already exists. Rotate or revoke it first.",
            EstimateShareOperationFailureReason.ShareNotFound => "No active secure share was found for this estimate.",
            EstimateShareOperationFailureReason.Unauthorized =>
                "You are not authorized to manage estimate shares.",
            EstimateShareOperationFailureReason.InvalidInput => "The share duration is invalid.",
            _ => "The secure share operation could not be completed.",
        };
}
