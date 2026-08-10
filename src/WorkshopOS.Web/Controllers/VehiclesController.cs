using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Customers;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Vehicles;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("vehicles")]
public sealed class VehiclesController : Controller
{
    private readonly IVehicleManagementService _vehicleManagementService;
    private readonly ICustomerManagementService _customerManagementService;
    private readonly IRepairOrderManagementService _repairOrderManagementService;
    private readonly IAuthorizationService _authorizationService;

    public VehiclesController(
        IVehicleManagementService vehicleManagementService,
        ICustomerManagementService customerManagementService,
        IRepairOrderManagementService repairOrderManagementService,
        IAuthorizationService authorizationService)
    {
        _vehicleManagementService = vehicleManagementService;
        _customerManagementService = customerManagementService;
        _repairOrderManagementService = repairOrderManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        Guid? currentCustomerId,
        int page = 1,
        int pageSize = VehicleListQuery.DefaultPageSize)
    {
        var result = await _vehicleManagementService.ListVehiclesAsync(new VehicleListQuery
        {
            Search = search,
            CurrentCustomerId = currentCustomerId,
            Page = page,
            PageSize = pageSize,
        });

        string? customerDisplayName = null;
        if (currentCustomerId.HasValue)
        {
            var customer = await _customerManagementService.GetCustomerDetailsAsync(currentCustomerId.Value);
            customerDisplayName = customer?.DisplayName;
        }

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.VehicleManager)).Succeeded;

        return View(new VehicleListViewModel
        {
            Search = search,
            CurrentCustomerId = currentCustomerId,
            CurrentCustomerDisplayName = customerDisplayName,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanManageVehicles = canManage,
            Vehicles = result.Items
                .Select(vehicle => new VehicleRowViewModel
                {
                    VehicleId = vehicle.VehicleId,
                    Make = vehicle.Make,
                    Model = vehicle.Model,
                    ModelYear = vehicle.ModelYear,
                    Vin = vehicle.Vin,
                    RegistrationPlate = vehicle.RegistrationPlate,
                    CurrentCustomerDisplayName = vehicle.CurrentCustomerDisplayName,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.VehicleManager)]
    [HttpGet("create")]
    public async Task<IActionResult> Create(Guid? currentCustomerId)
    {
        return View(new VehicleFormViewModel
        {
            CurrentCustomerId = currentCustomerId,
            CustomerOptions = await LoadCustomerOptionsAsync(),
        });
    }

    [Authorize(Policy = PolicyNames.VehicleManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VehicleFormViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            viewModel.CustomerOptions = await LoadCustomerOptionsAsync();
            return View(viewModel);
        }

        var result = await _vehicleManagementService.CreateVehicleAsync(new CreateVehicleCommand
        {
            Make = viewModel.Make,
            Model = viewModel.Model,
            ModelYear = viewModel.ModelYear,
            Vin = viewModel.Vin,
            RegistrationPlate = viewModel.RegistrationPlate,
            Color = viewModel.Color,
            CurrentCustomerId = viewModel.CurrentCustomerId,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            viewModel.CustomerOptions = await LoadCustomerOptionsAsync();
            return View(viewModel);
        }

        return RedirectToAction(nameof(Details), new { vehicleId = result.Value });
    }

    [HttpGet("{vehicleId:guid}")]
    public async Task<IActionResult> Details(Guid vehicleId)
    {
        var details = await _vehicleManagementService.GetVehicleDetailsAsync(vehicleId);
        if (details is null)
        {
            return NotFound();
        }

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.VehicleManager)).Succeeded;
        var latestRepairOrder = await _repairOrderManagementService.ListRepairOrdersAsync(new RepairOrderListQuery
        {
            VehicleId = vehicleId,
            PageSize = 1,
        });
        var latest = latestRepairOrder.Items.FirstOrDefault();

        return View(new VehicleDetailsViewModel
        {
            VehicleId = details.VehicleId,
            Make = details.Make,
            Model = details.Model,
            ModelYear = details.ModelYear,
            Vin = details.Vin,
            RegistrationPlate = details.RegistrationPlate,
            Color = details.Color,
            CurrentCustomerId = details.CurrentCustomerId,
            CurrentCustomerDisplayName = details.CurrentCustomerDisplayName,
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            RepairOrderCount = details.RepairOrderCount,
            LastRepairOrderOpenedAtUtc = details.LastRepairOrderOpenedAtUtc,
            LastRepairOrderStatus = latest?.Status,
            LastRepairOrderNumber = latest?.Number,
            LastRepairOrderId = latest?.RepairOrderId,
            UpcomingAppointmentCount = details.UpcomingAppointmentCount,
            NextAppointmentStartUtc = details.NextAppointmentStartUtc,
            CanManageVehicles = canManage,
        });
    }

    [Authorize(Policy = PolicyNames.VehicleManager)]
    [HttpGet("{vehicleId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid vehicleId)
    {
        var details = await _vehicleManagementService.GetVehicleDetailsAsync(vehicleId);
        if (details is null)
        {
            return NotFound();
        }

        return View(new VehicleFormViewModel
        {
            VehicleId = details.VehicleId,
            Make = details.Make,
            Model = details.Model,
            ModelYear = details.ModelYear,
            Vin = details.Vin,
            RegistrationPlate = details.RegistrationPlate,
            Color = details.Color,
        });
    }

    [Authorize(Policy = PolicyNames.VehicleManager)]
    [HttpPost("{vehicleId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid vehicleId, VehicleFormViewModel viewModel)
    {
        viewModel.VehicleId = vehicleId;

        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        var result = await _vehicleManagementService.UpdateVehicleAsync(new UpdateVehicleCommand
        {
            VehicleId = vehicleId,
            Make = viewModel.Make,
            Model = viewModel.Model,
            ModelYear = viewModel.ModelYear,
            Vin = viewModel.Vin,
            RegistrationPlate = viewModel.RegistrationPlate,
            Color = viewModel.Color,
        });

        if (!result.Success)
        {
            if (result.FailureReason == VehicleOperationFailureReason.VehicleNotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(viewModel);
        }

        return RedirectToAction(nameof(Details), new { vehicleId });
    }

    [Authorize(Policy = PolicyNames.VehicleManager)]
    [HttpGet("{vehicleId:guid}/reassign-customer")]
    public async Task<IActionResult> ReassignCustomer(Guid vehicleId)
    {
        var details = await _vehicleManagementService.GetVehicleDetailsAsync(vehicleId);
        if (details is null)
        {
            return NotFound();
        }

        return View(new VehicleReassignViewModel
        {
            VehicleId = details.VehicleId,
            VehicleSummary = FormatVehicleSummary(details.Make, details.Model, details.ModelYear),
            CurrentCustomerDisplayName = details.CurrentCustomerDisplayName,
            NewCurrentCustomerId = details.CurrentCustomerId,
            CustomerOptions = await LoadCustomerOptionsAsync(),
        });
    }

    [Authorize(Policy = PolicyNames.VehicleManager)]
    [HttpPost("{vehicleId:guid}/reassign-customer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReassignCustomer(Guid vehicleId, VehicleReassignViewModel model)
    {
        model.VehicleId = vehicleId;

        var result = await _vehicleManagementService.ReassignVehicleCustomerAsync(new ReassignVehicleCustomerCommand
        {
            VehicleId = vehicleId,
            NewCurrentCustomerId = model.NewCurrentCustomerId,
        });

        if (!result.Success)
        {
            if (result.FailureReason == VehicleOperationFailureReason.VehicleNotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            var details = await _vehicleManagementService.GetVehicleDetailsAsync(vehicleId);
            if (details is not null)
            {
                model.VehicleSummary = FormatVehicleSummary(details.Make, details.Model, details.ModelYear);
                model.CurrentCustomerDisplayName = details.CurrentCustomerDisplayName;
            }

            model.CustomerOptions = await LoadCustomerOptionsAsync();
            return View(model);
        }

        return RedirectToAction(nameof(Details), new { vehicleId });
    }

    private async Task<IReadOnlyList<CustomerOptionViewModel>> LoadCustomerOptionsAsync()
    {
        var customers = await _customerManagementService.ListCustomersAsync(new CustomerListQuery
        {
            PageSize = CustomerListQuery.MaxPageSize,
        });

        return customers.Items
            .Select(customer => new CustomerOptionViewModel
            {
                CustomerId = customer.CustomerId,
                DisplayName = customer.DisplayName,
            })
            .ToList();
    }

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private static string MapFailure(VehicleOperationFailureReason? reason) =>
        reason switch
        {
            VehicleOperationFailureReason.InvalidInput =>
                "Please check the vehicle details and try again.",
            VehicleOperationFailureReason.CustomerNotFound =>
                "The selected customer was not found in the current organization.",
            VehicleOperationFailureReason.OrganizationUnresolved =>
                "Organization context is not available.",
            _ => "Unable to save vehicle record.",
        };
}
