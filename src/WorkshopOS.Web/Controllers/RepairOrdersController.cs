using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Customers;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.Operations;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Application.Team;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Appointments;
using WorkshopOS.Web.Models.RepairOrders;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("repair-orders")]
public sealed class RepairOrdersController : Controller
{
    private readonly IRepairOrderManagementService _repairOrderManagementService;
    private readonly IInspectionManagementService _inspectionManagementService;
    private readonly IEstimateManagementService _estimateManagementService;
    private readonly IInvoiceManagementService _invoiceManagementService;
    private readonly IWorkshopOperationsService _workshopOperationsService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly ICustomerManagementService _customerManagementService;
    private readonly IVehicleManagementService _vehicleManagementService;
    private readonly IAuthorizationService _authorizationService;

    public RepairOrdersController(
        IRepairOrderManagementService repairOrderManagementService,
        IInspectionManagementService inspectionManagementService,
        IEstimateManagementService estimateManagementService,
        IInvoiceManagementService invoiceManagementService,
        IWorkshopOperationsService workshopOperationsService,
        ITeamManagementService teamManagementService,
        ICustomerManagementService customerManagementService,
        IVehicleManagementService vehicleManagementService,
        IAuthorizationService authorizationService)
    {
        _repairOrderManagementService = repairOrderManagementService;
        _inspectionManagementService = inspectionManagementService;
        _estimateManagementService = estimateManagementService;
        _invoiceManagementService = invoiceManagementService;
        _workshopOperationsService = workshopOperationsService;
        _teamManagementService = teamManagementService;
        _customerManagementService = customerManagementService;
        _vehicleManagementService = vehicleManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        RepairOrderStatus? status,
        Guid? workshopLocationId,
        Guid? customerId,
        Guid? vehicleId,
        int page = 1,
        int pageSize = RepairOrderListQuery.DefaultPageSize)
    {
        var result = await _repairOrderManagementService.ListRepairOrdersAsync(new RepairOrderListQuery
        {
            Search = search,
            Status = status,
            WorkshopLocationId = workshopLocationId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            Page = page,
            PageSize = pageSize,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.RepairOrderManager)).Succeeded;
        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new RepairOrderListViewModel
        {
            Search = search,
            Status = status,
            WorkshopLocationId = workshopLocationId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanManageRepairOrders = canManage,
            LocationOptions = locationOptions,
            RepairOrders = result.Items
                .Select(repairOrder => new RepairOrderRowViewModel
                {
                    RepairOrderId = repairOrder.RepairOrderId,
                    Number = repairOrder.Number,
                    Status = repairOrder.Status,
                    CustomerDisplayName = repairOrder.CustomerDisplayName,
                    VehicleSummary = repairOrder.VehicleSummary,
                    WorkshopLocationName = repairOrder.WorkshopLocationName,
                    OpenedAtUtc = repairOrder.OpenedAtUtc,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpGet("create")]
    public async Task<IActionResult> Create(
        Guid? customerId,
        Guid? vehicleId,
        Guid? workshopLocationId)
    {
        return View(await BuildFormViewModelAsync(new RepairOrderFormViewModel
        {
            CustomerId = customerId ?? Guid.Empty,
            VehicleId = vehicleId ?? Guid.Empty,
            WorkshopLocationId = workshopLocationId ?? Guid.Empty,
        }));
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RepairOrderFormViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormViewModelAsync(viewModel));
        }

        var result = await _repairOrderManagementService.CreateRepairOrderAsync(new CreateRepairOrderCommand
        {
            WorkshopLocationId = viewModel.WorkshopLocationId,
            CustomerId = viewModel.CustomerId,
            VehicleId = viewModel.VehicleId,
            CustomerConcern = viewModel.CustomerConcern,
            InternalNotes = viewModel.InternalNotes,
            Odometer = viewModel.Odometer,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(await BuildFormViewModelAsync(viewModel));
        }

        return RedirectToAction(nameof(Details), new { repairOrderId = result.Value });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("from-appointment/{appointmentId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromAppointment(Guid appointmentId)
    {
        var result = await _repairOrderManagementService.CreateRepairOrderFromAppointmentAsync(
            new CreateRepairOrderFromAppointmentCommand
            {
                AppointmentId = appointmentId,
            });

        if (!result.Success)
        {
            if (result.FailureReason == RepairOrderOperationFailureReason.AppointmentNotFound)
            {
                return NotFound();
            }

            TempData["RepairOrderError"] = MapFailure(result.FailureReason);
            return RedirectToAction("Details", "Appointments", new { appointmentId });
        }

        return RedirectToAction(nameof(Details), new { repairOrderId = result.Value });
    }

    [HttpGet("{repairOrderId:guid}")]
    public async Task<IActionResult> Details(Guid repairOrderId)
    {
        var details = await _repairOrderManagementService.GetRepairOrderDetailsAsync(repairOrderId);
        if (details is null)
        {
            return NotFound();
        }

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.RepairOrderManager)).Succeeded;
        var canManageInspections = (await _authorizationService.AuthorizeAsync(User, PolicyNames.InspectionManager)).Succeeded;
        var canManageEstimates = (await _authorizationService.AuthorizeAsync(User, PolicyNames.EstimateManager)).Succeeded;
        var canManageBilling = (await _authorizationService.AuthorizeAsync(User, PolicyNames.BillingManager)).Succeeded;
        var actorUserId = GetActorUserId();
        RepairOrderOperationsContext? operationsContext = null;
        if (actorUserId.HasValue)
        {
            operationsContext = await _workshopOperationsService.GetRepairOrderOperationsContextAsync(
                repairOrderId,
                actorUserId.Value);
        }

        var inspectionSummary = await _inspectionManagementService.GetRepairOrderInspectionSummaryAsync(repairOrderId);
        var inspectionList = await _inspectionManagementService.ListInspectionsAsync(new InspectionListQuery
        {
            RepairOrderId = repairOrderId,
            PageSize = InspectionListQuery.MaxPageSize,
        });

        var estimateSummary = await _estimateManagementService.GetRepairOrderEstimateSummaryAsync(repairOrderId);
        var estimateList = await _estimateManagementService.ListEstimatesAsync(new EstimateListQuery
        {
            RepairOrderId = repairOrderId,
            PageSize = EstimateListQuery.MaxPageSize,
        });

        var billingSummary = await _invoiceManagementService.GetRepairOrderBillingSummaryAsync(repairOrderId);
        var invoiceList = await _invoiceManagementService.ListInvoicesAsync(new InvoiceListQuery
        {
            RepairOrderId = repairOrderId,
            PageSize = InvoiceListQuery.MaxPageSize,
        });

        return View(new RepairOrderDetailsViewModel
        {
            RepairOrderId = details.RepairOrderId,
            Number = details.Number,
            Status = details.Status,
            WorkshopLocationId = details.WorkshopLocationId,
            WorkshopLocationName = details.WorkshopLocationName,
            AppointmentId = details.AppointmentId,
            CustomerId = details.CustomerId,
            CustomerDisplayName = details.CustomerDisplayName,
            VehicleId = details.VehicleId,
            VehicleSummary = details.VehicleSummary,
            CustomerConcern = details.CustomerConcern,
            InternalNotes = details.InternalNotes,
            Odometer = details.Odometer,
            OpenedAtUtc = details.OpenedAtUtc,
            CompletedAtUtc = details.CompletedAtUtc,
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            CanManageRepairOrders = canManage,
            CanStartWork = canManage && RepairOrderLifecyclePolicy.CanStartWork(details.Status),
            CanComplete = canManage && RepairOrderLifecyclePolicy.CanComplete(details.Status),
            CanCancel = canManage && RepairOrderLifecyclePolicy.CanCancel(details.Status),
            CanEditIntake = canManage && !RepairOrderLifecyclePolicy.IsTerminal(details.Status),
            Priority = operationsContext?.Priority ?? RepairOrderPriority.Normal,
            AssignedTechnicianDisplayName = operationsContext?.AssignedTechnicianDisplayName,
            AssignedTechnicianIsInactive = operationsContext?.AssignedTechnicianIsInactive ?? false,
            TechnicianWorkStatus = operationsContext?.TechnicianWorkStatus,
            CanManageOperations = operationsContext?.CanManageOperations ?? canManage,
            CanAssignTechnician = operationsContext?.CanAssign ?? false,
            CanReassignTechnician = operationsContext?.CanReassign ?? false,
            CanUnassignTechnician = operationsContext?.CanUnassign ?? false,
            CanChangePriority = operationsContext?.CanChangePriority ?? false,
            CanStartOwnWork = operationsContext?.CanStartOwnWork ?? false,
            CanCompleteOwnWork = operationsContext?.CanCompleteOwnWork ?? false,
            EligibleTechnicians = (operationsContext?.EligibleTechnicians ?? Array.Empty<EligibleTechnicianOption>())
                .Select(technician => new EligibleTechnicianOptionViewModel
                {
                    StaffMemberId = technician.StaffMemberId,
                    DisplayName = technician.DisplayName,
                })
                .ToList(),
            AssignmentHistory = (operationsContext?.AssignmentHistory ?? Array.Empty<AssignmentHistoryItem>())
                .Select(history => new AssignmentHistoryViewModel
                {
                    TechnicianDisplayName = history.TechnicianDisplayName,
                    WorkStatus = history.WorkStatus,
                    AssignedAtUtc = history.AssignedAtUtc,
                    UnassignedAtUtc = history.UnassignedAtUtc,
                })
                .ToList(),
            InspectionCount = inspectionSummary?.InspectionCount ?? 0,
            LatestInspectionId = inspectionSummary?.LatestInspectionId,
            LatestInspectionStatus = inspectionSummary?.LatestInspectionStatus,
            LatestInspectionCreatedAtUtc = inspectionSummary?.LatestInspectionCreatedAtUtc,
            LatestInspectionCompletedAtUtc = inspectionSummary?.LatestInspectionCompletedAtUtc,
            CanCreateInspection = canManageInspections
                                  && !RepairOrderLifecyclePolicy.IsTerminal(details.Status),
            Inspections = inspectionList.Items
                .Select(inspection => new RepairOrderInspectionRowViewModel
                {
                    InspectionId = inspection.InspectionId,
                    Status = inspection.Status,
                    TotalItems = inspection.TotalItems,
                    InspectedItems = inspection.InspectedItems,
                    CreatedAtUtc = inspection.CreatedAtUtc,
                    CompletedAtUtc = inspection.CompletedAtUtc,
                })
                .ToList(),
            EstimateCount = estimateSummary?.EstimateCount ?? 0,
            LatestEstimateId = estimateSummary?.LatestEstimateId,
            LatestEstimateStatus = estimateSummary?.LatestEstimateStatus,
            LatestEstimateTotal = estimateSummary?.LatestEstimateTotal,
            LatestEstimateCurrencyCode = estimateSummary?.LatestEstimateCurrencyCode,
            CanCreateEstimate = canManageEstimates
                                  && !RepairOrderLifecyclePolicy.IsTerminal(details.Status),
            CanManageEstimates = canManageEstimates,
            Estimates = estimateList.Items
                .Select(estimate => new RepairOrderEstimateRowViewModel
                {
                    EstimateId = estimate.EstimateId,
                    Number = estimate.Number,
                    Status = estimate.Status,
                    Total = estimate.Total,
                    CurrencyCode = estimate.CurrencyCode,
                    CreatedAtUtc = estimate.CreatedAtUtc,
                    SentAtUtc = estimate.SentAtUtc,
                })
                .ToList(),
            CurrentInvoiceId = billingSummary?.CurrentInvoiceId,
            CurrentInvoiceNumber = billingSummary?.CurrentInvoiceNumber,
            CurrentInvoiceStatus = billingSummary?.CurrentInvoiceStatus,
            InvoicePaymentState = billingSummary?.PaymentState,
            InvoiceTotal = billingSummary?.Total,
            InvoiceAmountPaid = billingSummary?.AmountPaid,
            InvoiceRemainingBalance = billingSummary?.RemainingBalance,
            CommerciallyClosed = billingSummary?.CommerciallyClosed ?? false,
            CanCreateInvoice = canManageBilling && (billingSummary?.CanCreateInvoice ?? false),
            CanCloseCommercially = canManageBilling && (billingSummary?.CanCloseCommercially ?? false),
            CanManageBilling = canManageBilling,
            Invoices = invoiceList.Items
                .Select(invoice => new RepairOrderInvoiceRowViewModel
                {
                    InvoiceId = invoice.InvoiceId,
                    Number = invoice.Number,
                    Status = invoice.Status,
                    PaymentState = invoice.PaymentState,
                    Total = invoice.Total,
                    CurrencyCode = invoice.CurrencyCode,
                    CreatedAtUtc = invoice.CreatedAtUtc,
                    IssuedAtUtc = invoice.IssuedAtUtc,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpGet("{repairOrderId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid repairOrderId)
    {
        var details = await _repairOrderManagementService.GetRepairOrderDetailsAsync(repairOrderId);
        if (details is null)
        {
            return NotFound();
        }

        if (RepairOrderLifecyclePolicy.IsTerminal(details.Status))
        {
            return RedirectToAction(nameof(Details), new { repairOrderId });
        }

        return View(new RepairOrderFormViewModel
        {
            RepairOrderId = details.RepairOrderId,
            CustomerConcern = details.CustomerConcern,
            InternalNotes = details.InternalNotes,
            Odometer = details.Odometer,
        });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("{repairOrderId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid repairOrderId, RepairOrderFormViewModel viewModel)
    {
        viewModel.RepairOrderId = repairOrderId;

        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        var result = await _repairOrderManagementService.UpdateRepairOrderIntakeAsync(new UpdateRepairOrderIntakeCommand
        {
            RepairOrderId = repairOrderId,
            CustomerConcern = viewModel.CustomerConcern,
            InternalNotes = viewModel.InternalNotes,
            Odometer = viewModel.Odometer,
        });

        if (!result.Success)
        {
            if (result.FailureReason == RepairOrderOperationFailureReason.RepairOrderNotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(viewModel);
        }

        return RedirectToAction(nameof(Details), new { repairOrderId });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("{repairOrderId:guid}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid repairOrderId)
    {
        var result = await _repairOrderManagementService.StartRepairOrderAsync(repairOrderId);
        if (!result.Success)
        {
            return result.FailureReason == RepairOrderOperationFailureReason.RepairOrderNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(Details), new { repairOrderId });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("{repairOrderId:guid}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(Guid repairOrderId)
    {
        var result = await _repairOrderManagementService.CompleteRepairOrderAsync(repairOrderId);
        if (!result.Success)
        {
            return result.FailureReason == RepairOrderOperationFailureReason.RepairOrderNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(Details), new { repairOrderId });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("{repairOrderId:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid repairOrderId)
    {
        var result = await _repairOrderManagementService.CancelRepairOrderAsync(repairOrderId);
        if (!result.Success)
        {
            return result.FailureReason == RepairOrderOperationFailureReason.RepairOrderNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(Details), new { repairOrderId });
    }

    private async Task<RepairOrderFormViewModel> BuildFormViewModelAsync(RepairOrderFormViewModel viewModel)
    {
        viewModel.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
        viewModel.CustomerOptions = (await _customerManagementService.ListCustomersAsync(new CustomerListQuery
        {
            PageSize = CustomerListQuery.MaxPageSize,
        })).Items
            .Select(customer => new CustomerOptionViewModel
            {
                CustomerId = customer.CustomerId,
                DisplayName = customer.DisplayName,
            })
            .ToList();

        viewModel.VehicleOptions = (await _vehicleManagementService.ListVehiclesAsync(new VehicleListQuery
        {
            PageSize = VehicleListQuery.MaxPageSize,
        })).Items
            .Select(vehicle => new VehicleOptionViewModel
            {
                VehicleId = vehicle.VehicleId,
                Summary = $"{vehicle.Make} {vehicle.Model}",
            })
            .ToList();

        return viewModel;
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string MapFailure(RepairOrderOperationFailureReason? failureReason) =>
        failureReason switch
        {
            RepairOrderOperationFailureReason.OrganizationUnresolved =>
                "Organization context is not resolved for this request.",
            RepairOrderOperationFailureReason.InvalidInput => "One or more fields are invalid.",
            RepairOrderOperationFailureReason.WorkshopLocationNotFound =>
                "The selected workshop location is not available.",
            RepairOrderOperationFailureReason.CustomerNotFound => "The selected customer is not available.",
            RepairOrderOperationFailureReason.VehicleNotFound => "The selected vehicle is not available.",
            RepairOrderOperationFailureReason.AppointmentNotFound => "The appointment could not be found.",
            RepairOrderOperationFailureReason.CustomerVehicleMismatch =>
                "The vehicle is not linked to the selected customer.",
            RepairOrderOperationFailureReason.DuplicateAppointmentRepairOrder =>
                "A repair order already exists for this appointment.",
            RepairOrderOperationFailureReason.ConcurrencyConflict =>
                "The repair order could not be created due to a concurrent update. Please try again.",
            RepairOrderOperationFailureReason.InvalidLifecycleTransition =>
                "That workflow action is not allowed for the current repair order status.",
            RepairOrderOperationFailureReason.RepairOrderNotFound => "The repair order could not be found.",
            _ => "The repair order operation could not be completed.",
        };
}
