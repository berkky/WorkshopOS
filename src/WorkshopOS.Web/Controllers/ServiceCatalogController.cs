using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Models.Catalog;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("catalog/services")]
public sealed class ServiceCatalogController : Controller
{
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IWebFailureMessages _messages;

    public ServiceCatalogController(
        IServiceCatalogService serviceCatalogService,
        IAuthorizationService authorizationService,
        IWebFailureMessages messages)
    {
        _serviceCatalogService = serviceCatalogService;
        _authorizationService = authorizationService;
        _messages = messages;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        bool? isActive,
        int page = 1,
        int pageSize = ServiceCatalogListQuery.DefaultPageSize)
    {
        var result = await _serviceCatalogService.ListServicesAsync(new ServiceCatalogListQuery
        {
            Search = search,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.CatalogManager)).Succeeded;

        return View(new ServiceCatalogListViewModel
        {
            Search = search,
            IsActive = isActive,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanManageCatalog = canManage,
            Services = result.Items
                .Select(item => new ServiceCatalogRowViewModel
                {
                    ServiceCatalogItemId = item.ServiceCatalogItemId,
                    Code = item.Code,
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
    public IActionResult Create() => View(new ServiceCatalogFormViewModel());

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceCatalogFormViewModel model)
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

        var result = await _serviceCatalogService.CreateServiceAsync(
            actorUserId.Value,
            new CreateServiceCatalogItemCommand
            {
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                DefaultUnitPrice = model.DefaultUnitPrice,
            });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, _messages.Catalog(result.FailureReason));
            return View(model);
        }

        return RedirectToAction(nameof(Edit), new { serviceCatalogItemId = result.Value });
    }

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpGet("{serviceCatalogItemId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid serviceCatalogItemId)
    {
        var details = await _serviceCatalogService.GetServiceDetailsAsync(serviceCatalogItemId);
        if (details is null)
        {
            return NotFound();
        }

        return View(new ServiceCatalogFormViewModel
        {
            ServiceCatalogItemId = details.ServiceCatalogItemId,
            Code = details.Code,
            Name = details.Name,
            Description = details.Description,
            DefaultUnitPrice = details.DefaultUnitPrice,
            IsActive = details.IsActive,
        });
    }

    [Authorize(Policy = PolicyNames.CatalogManager)]
    [HttpPost("{serviceCatalogItemId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid serviceCatalogItemId, ServiceCatalogFormViewModel model)
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

        var result = await _serviceCatalogService.UpdateServiceAsync(
            actorUserId.Value,
            new UpdateServiceCatalogItemCommand
            {
                ServiceCatalogItemId = serviceCatalogItemId,
                Code = model.Code,
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
