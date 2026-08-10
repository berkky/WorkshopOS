using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Billing;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
public sealed class InvoicesController : Controller
{
    private readonly IInvoiceManagementService _invoiceManagementService;
    private readonly IPaymentManagementService _paymentManagementService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IAuthorizationService _authorizationService;

    public InvoicesController(
        IInvoiceManagementService invoiceManagementService,
        IPaymentManagementService paymentManagementService,
        ITeamManagementService teamManagementService,
        IAuthorizationService authorizationService)
    {
        _invoiceManagementService = invoiceManagementService;
        _paymentManagementService = paymentManagementService;
        _teamManagementService = teamManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("/invoices")]
    public async Task<IActionResult> Index(
        string? search,
        InvoiceStatus? status,
        InvoicePaymentState? paymentState,
        Guid? workshopLocationId,
        Guid? repairOrderId,
        int page = 1,
        int pageSize = InvoiceListQuery.DefaultPageSize)
    {
        var result = await _invoiceManagementService.ListInvoicesAsync(new InvoiceListQuery
        {
            Search = search,
            Status = status,
            PaymentState = paymentState,
            WorkshopLocationId = workshopLocationId,
            RepairOrderId = repairOrderId,
            Page = page,
            PageSize = pageSize,
        });

        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new InvoiceListViewModel
        {
            Search = search,
            Status = status,
            PaymentState = paymentState,
            WorkshopLocationId = workshopLocationId,
            RepairOrderId = repairOrderId,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            LocationOptions = locationOptions,
            Invoices = result.Items
                .Select(invoice => new InvoiceRowViewModel
                {
                    InvoiceId = invoice.InvoiceId,
                    Number = invoice.Number,
                    Status = invoice.Status,
                    PaymentState = invoice.PaymentState,
                    CurrencyCode = invoice.CurrencyCode,
                    Total = invoice.Total,
                    AmountPaid = invoice.AmountPaid,
                    RepairOrderId = invoice.RepairOrderId,
                    RepairOrderNumber = invoice.RepairOrderNumber,
                    CustomerDisplayName = invoice.CustomerDisplayName,
                    VehicleSummary = invoice.VehicleSummary,
                    WorkshopLocationName = invoice.WorkshopLocationName,
                    CreatedAtUtc = invoice.CreatedAtUtc,
                    IssuedAtUtc = invoice.IssuedAtUtc,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/repair-orders/{repairOrderId:guid}/invoices")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.CreateInvoiceAsync(actorUserId.Value, repairOrderId);
        if (!result.Success)
        {
            if (result.FailureReason == InvoiceOperationFailureReason.RepairOrderNotFound)
            {
                return NotFound();
            }

            if (result.FailureReason == InvoiceOperationFailureReason.Unauthorized)
            {
                return Forbid();
            }

            TempData["InvoiceError"] = MapFailure(result.FailureReason);
            return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
        }

        return RedirectToAction(nameof(Edit), new { invoiceId = result.Value });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/estimates/{estimateId:guid}/invoice")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromEstimate(Guid estimateId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.CreateInvoiceFromApprovedEstimateAsync(
            actorUserId.Value,
            estimateId);

        if (!result.Success)
        {
            if (result.FailureReason is InvoiceOperationFailureReason.EstimateNotFound
                or InvoiceOperationFailureReason.RepairOrderNotFound)
            {
                return NotFound();
            }

            if (result.FailureReason == InvoiceOperationFailureReason.Unauthorized)
            {
                return Forbid();
            }

            TempData["InvoiceError"] = MapFailure(result.FailureReason);
            return RedirectToAction("Details", "Estimates", new { estimateId });
        }

        return RedirectToAction(nameof(Edit), new { invoiceId = result.Value });
    }

    [HttpGet("/invoices/{invoiceId:guid}")]
    public async Task<IActionResult> Details(Guid invoiceId)
    {
        var details = await _invoiceManagementService.GetInvoiceDetailsAsync(invoiceId);
        if (details is null)
        {
            return NotFound();
        }

        if (details.CanEditItems
            && (await _authorizationService.AuthorizeAsync(User, PolicyNames.BillingManager)).Succeeded)
        {
            return RedirectToAction(nameof(Edit), new { invoiceId });
        }

        var model = await MapDetailsViewModelAsync(details);
        return View(model);
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpGet("/invoices/{invoiceId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid invoiceId)
    {
        var details = await _invoiceManagementService.GetInvoiceDetailsAsync(invoiceId);
        if (details is null)
        {
            return NotFound();
        }

        if (!details.CanEditItems)
        {
            return RedirectToAction(nameof(Details), new { invoiceId });
        }

        return View(await MapDetailsViewModelAsync(details));
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(Guid invoiceId, InvoiceItemFormViewModel viewModel)
    {
        viewModel.InvoiceId = invoiceId;

        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.AddInvoiceItemAsync(
            actorUserId.Value,
            new AddInvoiceItemCommand
            {
                InvoiceId = invoiceId,
                Type = viewModel.Type,
                Description = viewModel.Description,
                Quantity = viewModel.Quantity,
                UnitPrice = viewModel.UnitPrice,
            });

        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { invoiceId });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/items/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditItem(Guid invoiceId, Guid itemId, InvoiceItemFormViewModel viewModel)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.UpdateInvoiceItemAsync(
            actorUserId.Value,
            new UpdateInvoiceItemCommand
            {
                InvoiceId = invoiceId,
                InvoiceItemId = itemId,
                Type = viewModel.Type,
                Description = viewModel.Description,
                Quantity = viewModel.Quantity,
                UnitPrice = viewModel.UnitPrice,
            });

        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { invoiceId });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/items/{itemId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid invoiceId, Guid itemId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.RemoveInvoiceItemAsync(
            actorUserId.Value,
            invoiceId,
            itemId);

        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { invoiceId });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/commercial-notes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCommercialNotes(
        Guid invoiceId,
        InvoiceCommercialNotesFormViewModel viewModel)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.UpdateCommercialNotesAsync(
            actorUserId.Value,
            invoiceId,
            viewModel.CommercialNotes);

        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Edit), new { invoiceId });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/issue")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(Guid invoiceId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _invoiceManagementService.IssueInvoiceAsync(actorUserId.Value, invoiceId);
        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
            return RedirectToAction(nameof(Edit), new { invoiceId });
        }

        return RedirectToAction(nameof(Details), new { invoiceId });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/void")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Void(Guid invoiceId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var details = await _invoiceManagementService.GetInvoiceDetailsAsync(invoiceId);
        var repairOrderId = details?.RepairOrderId;

        var result = await _invoiceManagementService.VoidInvoiceAsync(actorUserId.Value, invoiceId);
        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
            if (details?.CanEditItems == true)
            {
                return RedirectToAction(nameof(Edit), new { invoiceId });
            }

            return RedirectToAction(nameof(Details), new { invoiceId });
        }

        if (repairOrderId.HasValue)
        {
            return RedirectToAction("Details", "RepairOrders", new { repairOrderId = repairOrderId.Value });
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/invoices/{invoiceId:guid}/payments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(Guid invoiceId, RecordPaymentFormViewModel viewModel)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _paymentManagementService.RecordPaymentAsync(
            actorUserId.Value,
            new RecordPaymentCommand
            {
                InvoiceId = invoiceId,
                Amount = viewModel.Amount,
                PaymentMethod = viewModel.PaymentMethod,
                Reference = viewModel.Reference,
                Note = viewModel.Note,
            });

        if (!result.Success)
        {
            TempData["InvoiceError"] = MapPaymentFailure(result.FailureReason);
        }

        return RedirectToAction(nameof(Details), new { invoiceId });
    }

    [Authorize(Policy = PolicyNames.BillingManager)]
    [HttpPost("/repair-orders/{repairOrderId:guid}/commercial-close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseCommercially(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _paymentManagementService.CloseRepairOrderCommerciallyAsync(
            actorUserId.Value,
            repairOrderId);

        if (!result.Success)
        {
            TempData["InvoiceError"] = MapFailure(result.FailureReason);
        }

        return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
    }

    private async Task<InvoiceDetailsViewModel> MapDetailsViewModelAsync(InvoiceDetails details)
    {
        var canManageBilling = (await _authorizationService.AuthorizeAsync(User, PolicyNames.BillingManager)).Succeeded;

        return new InvoiceDetailsViewModel
        {
            InvoiceId = details.InvoiceId,
            Number = details.Number,
            Status = details.Status,
            PaymentState = details.PaymentState,
            RepairOrderId = details.RepairOrderId,
            RepairOrderNumber = details.RepairOrderNumber,
            CustomerDisplayName = details.CustomerDisplayName,
            VehicleSummary = details.VehicleSummary,
            WorkshopLocationName = details.WorkshopLocationName,
            SourceEstimateId = details.SourceEstimateId,
            SourceEstimateNumber = details.SourceEstimateNumber,
            CurrencyCode = details.CurrencyCode,
            Total = details.Total,
            AmountPaid = details.AmountPaid,
            RemainingBalance = details.RemainingBalance,
            CommercialNotes = details.CommercialNotes,
            Items = details.Items
                .Select(item => new InvoiceItemRowViewModel
                {
                    InvoiceItemId = item.InvoiceItemId,
                    Type = item.Type,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal,
                    SortOrder = item.SortOrder,
                })
                .ToList(),
            Payments = details.Payments
                .Select(payment => new PaymentRecordRowViewModel
                {
                    PaymentRecordId = payment.PaymentRecordId,
                    Amount = payment.Amount,
                    PaymentMethod = payment.PaymentMethod,
                    Reference = payment.Reference,
                    Note = payment.Note,
                    RecordedAtUtc = payment.RecordedAtUtc,
                })
                .ToList(),
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            IssuedAtUtc = details.IssuedAtUtc,
            VoidedAtUtc = details.VoidedAtUtc,
            RepairOrderCommerciallyClosed = details.RepairOrderCommerciallyClosed,
            CanEditItems = details.CanEditItems,
            CanIssue = details.CanIssue,
            CanVoid = details.CanVoid,
            CanRecordPayment = details.CanRecordPayment,
            CanManageBilling = canManageBilling,
        };
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string MapFailure(InvoiceOperationFailureReason? reason) =>
        reason switch
        {
            InvoiceOperationFailureReason.OrganizationUnresolved => "Organization context is not resolved.",
            InvoiceOperationFailureReason.InvoiceNotFound => "Invoice was not found.",
            InvoiceOperationFailureReason.RepairOrderNotFound => "Repair order was not found.",
            InvoiceOperationFailureReason.EstimateNotFound => "Estimate was not found.",
            InvoiceOperationFailureReason.Unauthorized => "You are not authorized to manage billing.",
            InvoiceOperationFailureReason.InvalidInput => "One or more invoice fields are invalid.",
            InvoiceOperationFailureReason.RepairOrderNotEligible =>
                "Invoices cannot be created for cancelled repair orders.",
            InvoiceOperationFailureReason.EstimateNotEligible =>
                "Only approved estimates can be converted to invoices.",
            InvoiceOperationFailureReason.InvalidLifecycleTransition =>
                "This invoice cannot be changed in its current status.",
            InvoiceOperationFailureReason.EmptyInvoice =>
                "At least one line item is required before issuing an invoice.",
            InvoiceOperationFailureReason.ConcurrencyConflict =>
                "The invoice was updated by another operation. Refresh and try again.",
            InvoiceOperationFailureReason.ItemNotFound => "Invoice line item was not found.",
            InvoiceOperationFailureReason.ItemLimitExceeded =>
                "This invoice has reached the maximum number of line items.",
            InvoiceOperationFailureReason.CurrentInvoiceExists =>
                "This repair order already has an active invoice.",
            InvoiceOperationFailureReason.SourceEstimateInvoiceExists =>
                "An active invoice already exists for this estimate.",
            InvoiceOperationFailureReason.HasPayments =>
                "Issued invoices with payments cannot be voided.",
            InvoiceOperationFailureReason.CommerciallyClosed =>
                "This repair order is commercially closed and billing cannot be changed.",
            InvoiceOperationFailureReason.RepairOrderNotCompleted =>
                "Only completed repair orders can be commercially closed.",
            InvoiceOperationFailureReason.InvoiceNotPaid =>
                "The current invoice must be fully paid before commercial close.",
            InvoiceOperationFailureReason.AlreadyCommerciallyClosed =>
                "This repair order is already commercially closed.",
            _ => "The invoice operation could not be completed.",
        };

    private static string MapPaymentFailure(PaymentOperationFailureReason? reason) =>
        reason switch
        {
            PaymentOperationFailureReason.OrganizationUnresolved => "Organization context is not resolved.",
            PaymentOperationFailureReason.Unauthorized => "You are not authorized to record payments.",
            PaymentOperationFailureReason.InvalidInput => "The payment details are invalid.",
            PaymentOperationFailureReason.InvoiceNotFound => "Invoice was not found.",
            PaymentOperationFailureReason.InvalidLifecycleTransition =>
                "Payments can only be recorded against issued invoices.",
            PaymentOperationFailureReason.Overpayment => "Payment amount would exceed the invoice balance.",
            PaymentOperationFailureReason.ConcurrencyConflict =>
                "The payment could not be recorded due to a concurrent update. Try again.",
            PaymentOperationFailureReason.CommerciallyClosed =>
                "This repair order is commercially closed and payments cannot be recorded.",
            _ => "The payment could not be recorded.",
        };
}
