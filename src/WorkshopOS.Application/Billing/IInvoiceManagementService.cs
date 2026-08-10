using WorkshopOS.Domain.Billing;

namespace WorkshopOS.Application.Billing;

public interface IInvoiceNumberGenerator
{
    string Generate(DateTimeOffset createdAtUtc);
}

public interface IInvoiceManagementService
{
    Task<InvoiceListResult> ListInvoicesAsync(
        InvoiceListQuery query,
        CancellationToken cancellationToken = default);

    Task<InvoiceDetails?> GetInvoiceDetailsAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<RepairOrderBillingSummary?> GetRepairOrderBillingSummaryAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult<Guid>> CreateInvoiceAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult<Guid>> CreateInvoiceFromApprovedEstimateAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult<Guid>> AddInvoiceItemAsync(
        Guid actorUserId,
        AddInvoiceItemCommand command,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult> UpdateInvoiceItemAsync(
        Guid actorUserId,
        UpdateInvoiceItemCommand command,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult> RemoveInvoiceItemAsync(
        Guid actorUserId,
        Guid invoiceId,
        Guid invoiceItemId,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult> UpdateCommercialNotesAsync(
        Guid actorUserId,
        Guid invoiceId,
        string? commercialNotes,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult> IssueInvoiceAsync(
        Guid actorUserId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult> VoidInvoiceAsync(
        Guid actorUserId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);
}

public interface IPaymentManagementService
{
    Task<PaymentOperationResult<Guid>> RecordPaymentAsync(
        Guid actorUserId,
        RecordPaymentCommand command,
        CancellationToken cancellationToken = default);

    Task<InvoiceOperationResult> CloseRepairOrderCommerciallyAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default);
}

public sealed record InvoiceListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public InvoiceStatus? Status { get; init; }

    public InvoicePaymentState? PaymentState { get; init; }

    public Guid? WorkshopLocationId { get; init; }

    public Guid? RepairOrderId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class InvoiceListResult
{
    public required IReadOnlyList<InvoiceListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class InvoiceListItem
{
    public required Guid InvoiceId { get; init; }

    public required string Number { get; init; }

    public required InvoiceStatus Status { get; init; }

    public required InvoicePaymentState PaymentState { get; init; }

    public required string CurrencyCode { get; init; }

    public required decimal Total { get; init; }

    public required decimal AmountPaid { get; init; }

    public required Guid RepairOrderId { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? IssuedAtUtc { get; init; }
}

public sealed class InvoiceDetails
{
    public required Guid InvoiceId { get; init; }

    public required string Number { get; init; }

    public required InvoiceStatus Status { get; init; }

    public required InvoicePaymentState PaymentState { get; init; }

    public required Guid RepairOrderId { get; init; }

    public required string RepairOrderNumber { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public Guid? SourceEstimateId { get; init; }

    public string? SourceEstimateNumber { get; init; }

    public required string CurrencyCode { get; init; }

    public required decimal Total { get; init; }

    public required decimal AmountPaid { get; init; }

    public required decimal RemainingBalance { get; init; }

    public string? CommercialNotes { get; init; }

    public required IReadOnlyList<InvoiceItemDetails> Items { get; init; }

    public required IReadOnlyList<PaymentRecordDetails> Payments { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? IssuedAtUtc { get; init; }

    public DateTimeOffset? VoidedAtUtc { get; init; }

    public bool RepairOrderCommerciallyClosed { get; init; }

    public bool CanEditItems { get; init; }

    public bool CanIssue { get; init; }

    public bool CanVoid { get; init; }

    public bool CanRecordPayment { get; init; }
}

public sealed class InvoiceItemDetails
{
    public required Guid InvoiceItemId { get; init; }

    public required InvoiceItemType Type { get; init; }

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }

    public required int SortOrder { get; init; }
}

public sealed class PaymentRecordDetails
{
    public required Guid PaymentRecordId { get; init; }

    public required decimal Amount { get; init; }

    public required PaymentMethod PaymentMethod { get; init; }

    public string? Reference { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed class RepairOrderBillingSummary
{
    public Guid? CurrentInvoiceId { get; init; }

    public string? CurrentInvoiceNumber { get; init; }

    public InvoiceStatus? CurrentInvoiceStatus { get; init; }

    public InvoicePaymentState? PaymentState { get; init; }

    public decimal? Total { get; init; }

    public decimal? AmountPaid { get; init; }

    public decimal? RemainingBalance { get; init; }

    public bool CommerciallyClosed { get; init; }

    public bool CanCreateInvoice { get; init; }

    public bool CanCloseCommercially { get; init; }
}

public sealed record AddInvoiceItemCommand
{
    public required Guid InvoiceId { get; init; }

    public InvoiceItemType Type { get; init; } = InvoiceItemType.Service;

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}

public sealed record UpdateInvoiceItemCommand
{
    public required Guid InvoiceId { get; init; }

    public required Guid InvoiceItemId { get; init; }

    public InvoiceItemType Type { get; init; } = InvoiceItemType.Service;

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}

public sealed record RecordPaymentCommand
{
    public required Guid InvoiceId { get; init; }

    public required decimal Amount { get; init; }

    public required PaymentMethod PaymentMethod { get; init; }

    public string? Reference { get; init; }

    public string? Note { get; init; }
}

public sealed class InvoiceOperationResult
{
    public bool Success { get; init; }

    public InvoiceOperationFailureReason? FailureReason { get; init; }

    public static InvoiceOperationResult Succeeded() => new() { Success = true };

    public static InvoiceOperationResult Failed(InvoiceOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class InvoiceOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public InvoiceOperationFailureReason? FailureReason { get; init; }

    public static InvoiceOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static InvoiceOperationResult<T> Failed(InvoiceOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class PaymentOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public PaymentOperationFailureReason? FailureReason { get; init; }

    public static PaymentOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static PaymentOperationResult<T> Failed(PaymentOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum InvoiceOperationFailureReason
{
    OrganizationUnresolved = 1,
    Unauthorized = 2,
    InvalidInput = 3,
    InvoiceNotFound = 4,
    RepairOrderNotFound = 5,
    EstimateNotFound = 6,
    RepairOrderNotEligible = 7,
    EstimateNotEligible = 8,
    InvalidLifecycleTransition = 9,
    EmptyInvoice = 10,
    ConcurrencyConflict = 11,
    ItemNotFound = 12,
    ItemLimitExceeded = 13,
    CurrentInvoiceExists = 14,
    SourceEstimateInvoiceExists = 15,
    HasPayments = 16,
    CommerciallyClosed = 17,
    RepairOrderNotCompleted = 18,
    InvoiceNotPaid = 19,
    AlreadyCommerciallyClosed = 20,
}

public enum PaymentOperationFailureReason
{
    OrganizationUnresolved = 1,
    Unauthorized = 2,
    InvalidInput = 3,
    InvoiceNotFound = 4,
    InvalidLifecycleTransition = 5,
    Overpayment = 6,
    ConcurrencyConflict = 7,
    CommerciallyClosed = 8,
}
