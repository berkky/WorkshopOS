using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Customers;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Customers;
using WorkshopOS.Web.Models.RepairOrders;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("customers")]
public sealed class CustomersController : Controller
{
    private readonly ICustomerManagementService _customerManagementService;
    private readonly IRepairOrderManagementService _repairOrderManagementService;
    private readonly IAuthorizationService _authorizationService;

    public CustomersController(
        ICustomerManagementService customerManagementService,
        IRepairOrderManagementService repairOrderManagementService,
        IAuthorizationService authorizationService)
    {
        _customerManagementService = customerManagementService;
        _repairOrderManagementService = repairOrderManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = CustomerListQuery.DefaultPageSize)
    {
        var result = await _customerManagementService.ListCustomersAsync(new CustomerListQuery
        {
            Search = search,
            Page = page,
            PageSize = pageSize,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.CustomerManager)).Succeeded;

        return View(new CustomerListViewModel
        {
            Search = search,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanManageCustomers = canManage,
            Customers = result.Items
                .Select(customer => new CustomerRowViewModel
                {
                    CustomerId = customer.CustomerId,
                    DisplayName = customer.DisplayName,
                    Email = customer.Email,
                    Phone = customer.Phone,
                    IsActive = customer.IsActive,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.CustomerManager)]
    [HttpGet("create")]
    public IActionResult Create()
    {
        return View(new CustomerFormViewModel());
    }

    [Authorize(Policy = PolicyNames.CustomerManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _customerManagementService.CreateCustomerAsync(new CreateCustomerCommand
        {
            DisplayName = model.DisplayName,
            Email = model.Email,
            Phone = model.Phone,
            Notes = model.Notes,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(model);
        }

        return RedirectToAction(nameof(Details), new { customerId = result.Value });
    }

    [HttpGet("{customerId:guid}")]
    public async Task<IActionResult> Details(Guid customerId)
    {
        var details = await _customerManagementService.GetCustomerDetailsAsync(customerId);
        if (details is null)
        {
            return NotFound();
        }

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.CustomerManager)).Succeeded;
        var repairOrders = await _repairOrderManagementService.ListRepairOrdersAsync(new RepairOrderListQuery
        {
            CustomerId = customerId,
            PageSize = 5,
        });

        return View(new CustomerDetailsViewModel
        {
            CustomerId = details.CustomerId,
            DisplayName = details.DisplayName,
            Email = details.Email,
            Phone = details.Phone,
            Notes = details.Notes,
            IsActive = details.IsActive,
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            VehicleCount = details.VehicleCount,
            UpcomingAppointmentCount = details.UpcomingAppointmentCount,
            RepairOrderCount = repairOrders.TotalCount,
            RecentRepairOrders = repairOrders.Items
                .Select(repairOrder => new CustomerRepairOrderSummaryViewModel
                {
                    RepairOrderId = repairOrder.RepairOrderId,
                    Number = repairOrder.Number,
                    Status = repairOrder.Status,
                    VehicleSummary = repairOrder.VehicleSummary,
                    OpenedAtUtc = repairOrder.OpenedAtUtc,
                })
                .ToList(),
            CanManageCustomers = canManage,
        });
    }

    [Authorize(Policy = PolicyNames.CustomerManager)]
    [HttpGet("{customerId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid customerId)
    {
        var details = await _customerManagementService.GetCustomerDetailsAsync(customerId);
        if (details is null)
        {
            return NotFound();
        }

        return View(new CustomerFormViewModel
        {
            CustomerId = details.CustomerId,
            DisplayName = details.DisplayName,
            Email = details.Email,
            Phone = details.Phone,
            Notes = details.Notes,
            IsActive = details.IsActive,
        });
    }

    [Authorize(Policy = PolicyNames.CustomerManager)]
    [HttpPost("{customerId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid customerId, CustomerFormViewModel model)
    {
        model.CustomerId = customerId;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _customerManagementService.UpdateCustomerAsync(new UpdateCustomerCommand
        {
            CustomerId = customerId,
            DisplayName = model.DisplayName,
            Email = model.Email,
            Phone = model.Phone,
            Notes = model.Notes,
            IsActive = model.IsActive,
        });

        if (!result.Success)
        {
            if (result.FailureReason == CustomerOperationFailureReason.CustomerNotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(model);
        }

        return RedirectToAction(nameof(Details), new { customerId });
    }

    [Authorize(Policy = PolicyNames.CustomerManager)]
    [HttpPost("{customerId:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid customerId)
    {
        var result = await _customerManagementService.DeactivateCustomerAsync(customerId);
        if (!result.Success)
        {
            return result.FailureReason == CustomerOperationFailureReason.CustomerNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(Details), new { customerId });
    }

    [Authorize(Policy = PolicyNames.CustomerManager)]
    [HttpPost("{customerId:guid}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid customerId)
    {
        var result = await _customerManagementService.ActivateCustomerAsync(customerId);
        if (!result.Success)
        {
            return result.FailureReason == CustomerOperationFailureReason.CustomerNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(Details), new { customerId });
    }

    private static string MapFailure(CustomerOperationFailureReason? reason) =>
        reason switch
        {
            CustomerOperationFailureReason.InvalidInput =>
                "Please check the customer details and try again.",
            CustomerOperationFailureReason.OrganizationUnresolved =>
                "Organization context is not available.",
            _ => "Unable to save customer record.",
        };
}
