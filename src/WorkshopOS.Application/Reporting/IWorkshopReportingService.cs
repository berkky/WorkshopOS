using WorkshopOS.Application.Billing;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Application.Reporting;

public interface IWorkshopReportingService
{
    Task<ReportingOperationResult<ExecutiveDashboardResult>> GetExecutiveDashboardAsync(
        ReportingQuery query,
        CancellationToken cancellationToken = default);

    Task<ReportingOperationResult<OperationalReportResult>> GetOperationalReportAsync(
        ReportingQuery query,
        CancellationToken cancellationToken = default);

    Task<ReportingOperationResult<CommercialReportResult>> GetCommercialReportAsync(
        ReportingQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class ReportingQuery
{
    public required Guid ActorUserId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public Guid? WorkshopLocationId { get; init; }
}

public enum ReportingFailureReason
{
    Unauthorized = 1,
    OrganizationNotResolved = 2,
    InvalidDateRange = 3,
    LocationNotFound = 4,
}

public sealed class ReportingOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public ReportingFailureReason? FailureReason { get; init; }

    public static ReportingOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static ReportingOperationResult<T> Failed(ReportingFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class ReportingContextHeader
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required string TimeZoneId { get; init; }

    public Guid? WorkshopLocationId { get; init; }

    public string? WorkshopLocationName { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed class MoneyBreakdownItem
{
    public required string CurrencyCode { get; init; }

    public required decimal Amount { get; init; }
}

public sealed class AppointmentStatusBreakdown
{
    public int Scheduled { get; init; }

    public int Confirmed { get; init; }

    public int CheckedIn { get; init; }

    public int Completed { get; init; }

    public int Cancelled { get; init; }

    public int NoShow { get; init; }

    public int Total =>
        Scheduled + Confirmed + CheckedIn + Completed + Cancelled + NoShow;
}

public sealed class InvoicePaymentStateBreakdown
{
    public int Unpaid { get; init; }

    public int PartiallyPaid { get; init; }

    public int Paid { get; init; }
}

public sealed class TechnicianWorkloadItem
{
    public required Guid StaffMemberId { get; init; }

    public required string DisplayName { get; init; }

    public string? WorkshopLocationName { get; init; }

    public int ActiveAssignedJobs { get; init; }

    public int InProgressJobs { get; init; }

    public int WorkCompletedAssignmentsInPeriod { get; init; }
}

public sealed class LocationOperationalSummary
{
    public required Guid WorkshopLocationId { get; init; }

    public required string Name { get; init; }

    public int ActiveRepairOrders { get; init; }

    public int UnassignedRepairOrders { get; init; }

    public int UrgentRepairOrders { get; init; }

    public int AppointmentsInPeriod { get; init; }

    public int CompletedJobsInPeriod { get; init; }

    public int OpenDviInspections { get; init; }
}

public sealed class CurrentWorkshopSnapshot
{
    public int ActiveRepairOrders { get; init; }

    public int UnassignedRepairOrders { get; init; }

    public int UrgentRepairOrders { get; init; }

    public int TechniciansCurrentlyWorking { get; init; }

    public int OpenDviInspections { get; init; }

    public int EstimatesAwaitingDecision { get; init; }

    public int OutstandingInvoices { get; init; }

    public IReadOnlyList<MoneyBreakdownItem> CurrentOutstandingAmounts { get; init; } =
        Array.Empty<MoneyBreakdownItem>();

    public int CommerciallyClosableJobs { get; init; }
}

public sealed class SelectedPeriodPerformance
{
    public int AppointmentsScheduled { get; init; }

    public AppointmentStatusBreakdown AppointmentStatusBreakdown { get; init; } = new();

    public int RepairOrdersOpened { get; init; }

    public int RepairOrdersCompleted { get; init; }

    public int InspectionsCompleted { get; init; }

    public int CriticalInspectionFindings { get; init; }

    public int EstimatesPresented { get; init; }

    public int EstimatesApproved { get; init; }

    public int EstimatesDeclined { get; init; }

    public decimal? DecisionApprovalRate { get; init; }

    public int InvoicesIssued { get; init; }

    public IReadOnlyList<MoneyBreakdownItem> RecordedPayments { get; init; } =
        Array.Empty<MoneyBreakdownItem>();

    public IReadOnlyList<MoneyBreakdownItem> InvoicedAmounts { get; init; } =
        Array.Empty<MoneyBreakdownItem>();

    public int CommerciallyClosedJobs { get; init; }
}

public sealed class EstimateDecisionSourceBreakdown
{
    public int PortalApprovalsInPeriod { get; init; }

    public int PortalDeclinesInPeriod { get; init; }

    public int StaffApprovalsInPeriod { get; init; }

    public int StaffDeclinesInPeriod { get; init; }
}

public sealed class ExecutiveDashboardResult
{
    public required ReportingContextHeader Header { get; init; }

    public required CurrentWorkshopSnapshot CurrentSnapshot { get; init; }

    public required SelectedPeriodPerformance SelectedPeriod { get; init; }
}

public sealed class OperationalReportResult
{
    public required ReportingContextHeader Header { get; init; }

    public required CurrentWorkshopSnapshot CurrentSnapshot { get; init; }

    public required SelectedPeriodPerformance SelectedPeriod { get; init; }

    public IReadOnlyDictionary<RepairOrderStatus, int> RepairOrderStatusBreakdown { get; init; } =
        new Dictionary<RepairOrderStatus, int>();

    public IReadOnlyDictionary<RepairOrderPriority, int> RepairOrderPriorityBreakdown { get; init; } =
        new Dictionary<RepairOrderPriority, int>();

    public int ActiveAssignedRepairOrders { get; init; }

    public int ActiveUnassignedRepairOrders { get; init; }

    public IReadOnlyList<TechnicianWorkloadItem> TechnicianWorkload { get; init; } =
        Array.Empty<TechnicianWorkloadItem>();

    public IReadOnlyList<LocationOperationalSummary> LocationBreakdown { get; init; } =
        Array.Empty<LocationOperationalSummary>();
}

public sealed class CommercialReportResult
{
    public required ReportingContextHeader Header { get; init; }

    public required CurrentWorkshopSnapshot CurrentSnapshot { get; init; }

    public required SelectedPeriodPerformance SelectedPeriod { get; init; }

    public EstimateDecisionSourceBreakdown? DecisionSourceBreakdown { get; init; }

    public required InvoicePaymentStateBreakdown CurrentInvoicePaymentStateBreakdown { get; init; }
}
