using System.ComponentModel.DataAnnotations;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Billing;

namespace WorkshopOS.Web.Models.Billing;

public sealed class InvoiceListViewModel
{
    public IReadOnlyList<InvoiceRowViewModel> Invoices { get; set; } = Array.Empty<InvoiceRowViewModel>();

    public string? Search { get; set; }

    public InvoiceStatus? Status { get; set; }

    public InvoicePaymentState? PaymentState { get; set; }

    public Guid? WorkshopLocationId { get; set; }

    public Guid? RepairOrderId { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class InvoiceRowViewModel
{
    public Guid InvoiceId { get; set; }

    public string Number { get; set; } = string.Empty;

    public InvoiceStatus Status { get; set; }

    public InvoicePaymentState PaymentState { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public decimal AmountPaid { get; set; }

    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? IssuedAtUtc { get; set; }
}

public sealed class InvoiceDetailsViewModel
{
    public Guid InvoiceId { get; set; }

    public string Number { get; set; } = string.Empty;

    public InvoiceStatus Status { get; set; }

    public InvoicePaymentState PaymentState { get; set; }

    public Guid RepairOrderId { get; set; }

    public string RepairOrderNumber { get; set; } = string.Empty;

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string VehicleSummary { get; set; } = string.Empty;

    public string WorkshopLocationName { get; set; } = string.Empty;

    public Guid? SourceEstimateId { get; set; }

    public string? SourceEstimateNumber { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public decimal AmountPaid { get; set; }

    public decimal RemainingBalance { get; set; }

    public string? CommercialNotes { get; set; }

    public IReadOnlyList<InvoiceItemRowViewModel> Items { get; set; } =
        Array.Empty<InvoiceItemRowViewModel>();

    public IReadOnlyList<PaymentRecordRowViewModel> Payments { get; set; } =
        Array.Empty<PaymentRecordRowViewModel>();

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? IssuedAtUtc { get; set; }

    public DateTimeOffset? VoidedAtUtc { get; set; }

    public bool RepairOrderCommerciallyClosed { get; set; }

    public bool CanEditItems { get; set; }

    public bool CanIssue { get; set; }

    public bool CanVoid { get; set; }

    public bool CanRecordPayment { get; set; }

    public bool CanManageBilling { get; set; }
}

public sealed class InvoiceItemRowViewModel
{
    public Guid InvoiceItemId { get; set; }

    public InvoiceItemType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}

public sealed class PaymentRecordRowViewModel
{
    public Guid PaymentRecordId { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class InvoiceItemFormViewModel
{
    public Guid InvoiceId { get; set; }

    public Guid? InvoiceItemId { get; set; }

    [Display(Name = "Type")]
    public InvoiceItemType Type { get; set; } = InvoiceItemType.Service;

    [Required]
    [MaxLength(InvoiceInputValidator.MaxDescriptionLength)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; } = 1;

    [Required]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }
}

public sealed class InvoiceCommercialNotesFormViewModel
{
    public Guid InvoiceId { get; set; }

    [MaxLength(InvoiceInputValidator.MaxCommercialNotesLength)]
    [Display(Name = "Commercial notes")]
    public string? CommercialNotes { get; set; }
}

public sealed class RecordPaymentFormViewModel
{
    public Guid InvoiceId { get; set; }

    [Required]
    [Display(Name = "Amount")]
    public decimal Amount { get; set; }

    [Display(Name = "Payment method")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [MaxLength(InvoiceInputValidator.MaxReferenceLength)]
    [Display(Name = "Reference")]
    public string? Reference { get; set; }

    [MaxLength(InvoiceInputValidator.MaxNoteLength)]
    [Display(Name = "Note")]
    public string? Note { get; set; }
}
