using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Models.Catalog;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("catalog/parts")]
public sealed class PartCatalogController : Controller
{
    private readonly IPartCatalogService _partCatalogService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IWebFailureMessages _messages;

    public PartCatalogController(
        IPartCatalogService partCatalogService,
        IAuthorizationService authorizationService,
        IWebFailureMessages messages)
    {
        _partCatalogService = partCatalogService;
        _authorizationService = authorizationService;
        _messages = messages;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        bool? isActive,
        int page = 1,
        int pageSize = PartCatalogListQuery.DefaultPageSize)
    {
        var result = await _partCatalogService.ListPartsAsync(new PartCatalogListQuery
        {
            Search = search,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.CatalogManager)).Succeeded;

        return View(new PartCatalogListViewModel
        {
            Search = search,
            IsActive = isActive,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanManageCatalog = canManage,
            Parts = result.Items
                .Select(item => new PartCatalogRowViewModel
                {
                    PartCatalogItemId = item.PartCatalogItemId,
                    Sku = item.Sku,
                    Name = item.Name,
                    DefaultUnitPrice = item.DefaultUnitPrice,
                    CurrencyCode = item.CurrencyCode,
                    IsActive = item.IsActive,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpGet("create")]
    public IActionResult Create() => View(new PartCatalogFormViewModel());

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PartCatalogFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _partCatalogService.CreatePartAsync(
            actorUserId.Value,
            new CreatePartCatalogItemCommand
            {
                Sku = model.Sku,
                Name = model.Name,
                Description = model.Description,
                DefaultUnitPrice = model.DefaultUnitPrice,
            });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, _messages.Catalog(result.FailureReason));
            return View(model);
        }

        return RedirectToAction(nameof(Edit), new { partCatalogItemId = result.Value });
    }

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpGet("{partCatalogItemId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid partCatalogItemId)
    {
        var details = await _partCatalogService.GetPartDetailsAsync(partCatalogItemId);
        if (details is null)
        {
            return NotFound();
        }

        return View(new PartCatalogFormViewModel
        {
            PartCatalogItemId = details.PartCatalogItemId,
            Sku = details.Sku,
            Name = details.Name,
            Description = details.Description,
            DefaultUnitPrice = details.DefaultUnitPrice,
            IsActive = details.IsActive,
        });
    }

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpPost("{partCatalogItemId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid partCatalogItemId, PartCatalogFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _partCatalogService.UpdatePartAsync(
            actorUserId.Value,
            new UpdatePartCatalogItemCommand
            {
                PartCatalogItemId = partCatalogItemId,
                Sku = model.Sku,
                Name = model.Name,
                Description = model.Description,
                DefaultUnitPrice = model.DefaultUnitPrice,
                IsActive = model.IsActive,
            });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, _messages.Catalog(result.FailureReason));
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

}
