using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Reporting;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Reporting;

public sealed class WorkshopReportingService : IWorkshopReportingService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public WorkshopReportingService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public Task<ReportingOperationResult<ExecutiveDashboardResult>> GetExecutiveDashboardAsync(
        ReportingQuery query,
        CancellationToken cancellationToken = default) =>
        BuildReportAsync(
            query,
            async (context, ct) =>
            {
                var snapshot = await BuildCurrentSnapshotAsync(context, ct);
                var period = await BuildSelectedPeriodPerformanceAsync(context, ct);

                return new ExecutiveDashboardResult
                {
                    Header = context.Header,
                    CurrentSnapshot = snapshot,
                    SelectedPeriod = period,
                };
            },
            cancellationToken);

    public async Task<ReportingOperationResult<OperationalReportResult>> GetOperationalReportAsync(
        ReportingQuery query,
        CancellationToken cancellationToken = default)
    {
        return await BuildReportAsync(
            query,
            async (context, ct) =>
            {
                var snapshot = await BuildCurrentSnapshotAsync(context, ct);
                var period = await BuildSelectedPeriodPerformanceAsync(context, ct);
                var statusBreakdown = await BuildActiveRepairOrderStatusBreakdownAsync(context, ct);
                var priorityBreakdown = await BuildActiveRepairOrderPriorityBreakdownAsync(context, ct);
                var assignment = await BuildActiveAssignmentCountsAsync(context, ct);
                var technicianWorkload = await BuildTechnicianWorkloadAsync(context, ct);
                var locationBreakdown = await BuildLocationBreakdownAsync(context, ct);

                return new OperationalReportResult
                {
                    Header = context.Header,
                    CurrentSnapshot = snapshot,
                    SelectedPeriod = period,
                    RepairOrderStatusBreakdown = statusBreakdown,
                    RepairOrderPriorityBreakdown = priorityBreakdown,
                    ActiveAssignedRepairOrders = assignment.Assigned,
                    ActiveUnassignedRepairOrders = assignment.Unassigned,
                    TechnicianWorkload = technicianWorkload,
                    LocationBreakdown = locationBreakdown,
                };
            },
            cancellationToken);
    }

    public async Task<ReportingOperationResult<CommercialReportResult>> GetCommercialReportAsync(
        ReportingQuery query,
        CancellationToken cancellationToken = default)
    {
        return await BuildReportAsync(
            query,
            async (context, ct) =>
            {
                var snapshot = await BuildCurrentSnapshotAsync(context, ct);
                var period = await BuildSelectedPeriodPerformanceAsync(context, ct);
                var paymentStateBreakdown = await BuildCurrentInvoicePaymentStateBreakdownAsync(context, ct);
                var decisionSource = await BuildDecisionSourceBreakdownAsync(context, ct);

                return new CommercialReportResult
                {
                    Header = context.Header,
                    CurrentSnapshot = snapshot,
                    SelectedPeriod = period,
                    DecisionSourceBreakdown = decisionSource,
                    CurrentInvoicePaymentStateBreakdown = paymentStateBreakdown,
                };
            },
            cancellationToken);
    }

    private async Task<ReportingOperationResult<T>> BuildReportAsync<T>(
        ReportingQuery query,
        Func<ReportingExecutionContext, CancellationToken, Task<T>> build,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareExecutionContextAsync(query, cancellationToken);
        if (prepared.FailureReason is not null)
        {
            return ReportingOperationResult<T>.Failed(prepared.FailureReason.Value);
        }

        var value = await build(prepared.Context!, cancellationToken);
        return ReportingOperationResult<T>.Succeeded(value);
    }

    private async Task<(ReportingExecutionContext? Context, ReportingFailureReason? FailureReason)> PrepareExecutionContextAsync(
        ReportingQuery query,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return (null, ReportingFailureReason.OrganizationNotResolved);
        }

        if (!await CanActorViewReportingAsync(query.ActorUserId, organizationId, cancellationToken))
        {
            return (null, ReportingFailureReason.Unauthorized);
        }

        var organization = await _dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.TimeZoneId })
            .SingleAsync(cancellationToken);

        string? locationName = null;
        string? locationTimeZoneId = null;
        if (query.WorkshopLocationId.HasValue)
        {
            var location = await _dbContext.WorkshopLocations.AsNoTracking()
                .Where(candidate => candidate.Id == query.WorkshopLocationId.Value)
                .Select(candidate => new
                {
                    candidate.Name,
                    candidate.TimeZoneId,
                    candidate.OrganizationId,
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (location is null || location.OrganizationId != organizationId)
            {
                return (null, ReportingFailureReason.LocationNotFound);
            }

            locationName = location.Name;
            locationTimeZoneId = location.TimeZoneId;
        }

        var effectiveTimeZoneId = string.IsNullOrWhiteSpace(locationTimeZoneId)
            ? organization.TimeZoneId
            : locationTimeZoneId!;

        var today = ReportingPeriodResolver.ResolveTodayInTimeZone(
            _timeProvider.GetUtcNow(),
            effectiveTimeZoneId);

        if (!ReportingPeriodResolver.TryResolvePeriod(
                query.From,
                query.To,
                today,
                out var period,
                out var dateFailure))
        {
            return (null, dateFailure);
        }

        var (startUtc, endUtcExclusive) = ReportingPeriodResolver.ConvertToUtcBounds(
            period,
            effectiveTimeZoneId);

        var header = new ReportingContextHeader
        {
            From = period.From,
            To = period.To,
            TimeZoneId = effectiveTimeZoneId,
            WorkshopLocationId = query.WorkshopLocationId,
            WorkshopLocationName = locationName,
            GeneratedAtUtc = _timeProvider.GetUtcNow(),
        };

        return (new ReportingExecutionContext(
            organizationId,
            query.WorkshopLocationId,
            startUtc,
            endUtcExclusive,
            header), null);
    }

    private async Task<CurrentWorkshopSnapshot> BuildCurrentSnapshotAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var activeRepairOrders = FilterRepairOrders(context, activeOnly: true);
        var activeRepairOrderCount = await activeRepairOrders.CountAsync(cancellationToken);

        var activeAssignmentRepairOrderIds = _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment => assignment.UnassignedAtUtc == null)
            .Select(assignment => assignment.RepairOrderId);

        var unassignedCount = await activeRepairOrders
            .Where(repairOrder => !activeAssignmentRepairOrderIds.Contains(repairOrder.Id))
            .CountAsync(cancellationToken);

        var urgentCount = await activeRepairOrders
            .Where(repairOrder => repairOrder.Priority == RepairOrderPriority.Urgent)
            .CountAsync(cancellationToken);

        var techniciansWorking = await _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment =>
                assignment.UnassignedAtUtc == null
                && assignment.WorkStatus == TechnicianWorkStatus.InProgress)
            .Join(
                activeRepairOrders,
                assignment => assignment.RepairOrderId,
                repairOrder => repairOrder.Id,
                (assignment, _) => assignment.StaffMemberId)
            .Distinct()
            .CountAsync(cancellationToken);

        var openInspections = await FilterInspectionsViaRepairOrder(context)
            .Where(inspection =>
                inspection.Status == InspectionStatus.Draft
                || inspection.Status == InspectionStatus.InProgress)
            .CountAsync(cancellationToken);

        var estimatesAwaitingDecision = await FilterEstimatesViaRepairOrder(context)
            .Where(estimate => estimate.Status == EstimateStatus.Sent)
            .CountAsync(cancellationToken);

        var issuedInvoices = await LoadCurrentIssuedInvoicesAsync(context, cancellationToken);
        var financials = await LoadInvoiceFinancialsAsync(issuedInvoices, cancellationToken);

        var outstandingInvoices = 0;
        var outstandingAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in issuedInvoices)
        {
            var total = financials.Totals.GetValueOrDefault(invoice.Id, 0m);
            var paid = financials.Paid.GetValueOrDefault(invoice.Id, 0m);
            var outstanding = total - paid;
            if (outstanding <= 0)
            {
                continue;
            }

            outstandingInvoices++;
            outstandingAmounts[invoice.CurrencyCode] =
                outstandingAmounts.GetValueOrDefault(invoice.CurrencyCode, 0m) + outstanding;
        }

        var commerciallyClosable = await CountCommerciallyClosableJobsAsync(context, cancellationToken);

        return new CurrentWorkshopSnapshot
        {
            ActiveRepairOrders = activeRepairOrderCount,
            UnassignedRepairOrders = unassignedCount,
            UrgentRepairOrders = urgentCount,
            TechniciansCurrentlyWorking = techniciansWorking,
            OpenDviInspections = openInspections,
            EstimatesAwaitingDecision = estimatesAwaitingDecision,
            OutstandingInvoices = outstandingInvoices,
            CurrentOutstandingAmounts = ToMoneyBreakdown(outstandingAmounts),
            CommerciallyClosableJobs = commerciallyClosable,
        };
    }

    private async Task<SelectedPeriodPerformance> BuildSelectedPeriodPerformanceAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var appointmentsInPeriod = FilterAppointments(context)
            .Where(appointment =>
                appointment.ScheduledStartUtc >= context.StartUtc
                && appointment.ScheduledStartUtc < context.EndUtcExclusive);

        var appointmentStatusBreakdown = await appointmentsInPeriod
            .GroupBy(appointment => appointment.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var appointmentsScheduled = appointmentStatusBreakdown.Sum(item => item.Count);

        var repairOrdersOpened = await FilterRepairOrders(context)
            .Where(repairOrder =>
                repairOrder.OpenedAtUtc >= context.StartUtc
                && repairOrder.OpenedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var repairOrdersCompleted = await FilterRepairOrders(context)
            .Where(repairOrder =>
                repairOrder.CompletedAtUtc.HasValue
                && repairOrder.CompletedAtUtc >= context.StartUtc
                && repairOrder.CompletedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var inspectionsCompleted = await FilterInspectionsViaRepairOrder(context)
            .Where(inspection =>
                inspection.CompletedAtUtc.HasValue
                && inspection.CompletedAtUtc >= context.StartUtc
                && inspection.CompletedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var criticalFindings = await FilterInspectionsViaRepairOrder(context)
            .Where(inspection =>
                inspection.CompletedAtUtc.HasValue
                && inspection.CompletedAtUtc >= context.StartUtc
                && inspection.CompletedAtUtc < context.EndUtcExclusive)
            .Join(
                _dbContext.InspectionItems.AsNoTracking()
                    .Where(item => item.Condition == InspectionCondition.Critical),
                inspection => inspection.Id,
                item => item.InspectionId,
                (_, item) => item)
            .CountAsync(cancellationToken);

        var estimatesPresented = await FilterEstimatesViaRepairOrder(context)
            .Where(estimate =>
                estimate.SentAtUtc.HasValue
                && estimate.SentAtUtc >= context.StartUtc
                && estimate.SentAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var estimatesApproved = await FilterEstimatesViaRepairOrder(context)
            .Where(estimate =>
                estimate.ApprovedAtUtc.HasValue
                && estimate.ApprovedAtUtc >= context.StartUtc
                && estimate.ApprovedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var estimatesDeclined = await FilterEstimatesViaRepairOrder(context)
            .Where(estimate =>
                estimate.DeclinedAtUtc.HasValue
                && estimate.DeclinedAtUtc >= context.StartUtc
                && estimate.DeclinedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var decisionTotal = estimatesApproved + estimatesDeclined;
        decimal? approvalRate = decisionTotal == 0
            ? null
            : decimal.Round((decimal)estimatesApproved / decisionTotal * 100m, 1, MidpointRounding.AwayFromZero);

        var invoicesIssued = await FilterInvoicesViaRepairOrder(context)
            .Where(invoice =>
                invoice.Status == InvoiceStatus.Issued
                && invoice.IssuedAtUtc.HasValue
                && invoice.IssuedAtUtc >= context.StartUtc
                && invoice.IssuedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        var invoicedAmounts = await SumInvoicedAmountsInPeriodAsync(context, cancellationToken);
        var recordedPayments = await SumRecordedPaymentsInPeriodAsync(context, cancellationToken);

        var commerciallyClosed = await FilterRepairOrders(context)
            .Where(repairOrder =>
                repairOrder.CommerciallyClosedAtUtc.HasValue
                && repairOrder.CommerciallyClosedAtUtc >= context.StartUtc
                && repairOrder.CommerciallyClosedAtUtc < context.EndUtcExclusive)
            .CountAsync(cancellationToken);

        return new SelectedPeriodPerformance
        {
            AppointmentsScheduled = appointmentsScheduled,
            AppointmentStatusBreakdown = BuildAppointmentStatusBreakdown(
                appointmentStatusBreakdown.Select(item => (item.Status, item.Count))),
            RepairOrdersOpened = repairOrdersOpened,
            RepairOrdersCompleted = repairOrdersCompleted,
            InspectionsCompleted = inspectionsCompleted,
            CriticalInspectionFindings = criticalFindings,
            EstimatesPresented = estimatesPresented,
            EstimatesApproved = estimatesApproved,
            EstimatesDeclined = estimatesDeclined,
            DecisionApprovalRate = approvalRate,
            InvoicesIssued = invoicesIssued,
            RecordedPayments = recordedPayments,
            InvoicedAmounts = invoicedAmounts,
            CommerciallyClosedJobs = commerciallyClosed,
        };
    }

    private async Task<Dictionary<RepairOrderStatus, int>> BuildActiveRepairOrderStatusBreakdownAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var rows = await FilterRepairOrders(context, activeOnly: true)
            .GroupBy(repairOrder => repairOrder.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Status, row => row.Count);
    }

    private async Task<Dictionary<RepairOrderPriority, int>> BuildActiveRepairOrderPriorityBreakdownAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var rows = await FilterRepairOrders(context, activeOnly: true)
            .GroupBy(repairOrder => repairOrder.Priority)
            .Select(group => new { Priority = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Priority, row => row.Count);
    }

    private async Task<(int Assigned, int Unassigned)> BuildActiveAssignmentCountsAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var activeRepairOrders = FilterRepairOrders(context, activeOnly: true);
        var activeAssignmentRepairOrderIds = _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment => assignment.UnassignedAtUtc == null)
            .Select(assignment => assignment.RepairOrderId);

        var unassigned = await activeRepairOrders
            .Where(repairOrder => !activeAssignmentRepairOrderIds.Contains(repairOrder.Id))
            .CountAsync(cancellationToken);

        var assigned = await activeRepairOrders
            .Where(repairOrder => activeAssignmentRepairOrderIds.Contains(repairOrder.Id))
            .CountAsync(cancellationToken);

        return (assigned, unassigned);
    }

    private async Task<IReadOnlyList<TechnicianWorkloadItem>> BuildTechnicianWorkloadAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var technicians = await _dbContext.StaffMembers.AsNoTracking()
            .Where(staff =>
                staff.Status == StaffStatus.Active
                && staff.Position == StaffPosition.Technician)
            .Select(staff => new
            {
                staff.Id,
                staff.DisplayName,
            })
            .ToListAsync(cancellationToken);

        if (technicians.Count == 0)
        {
            return Array.Empty<TechnicianWorkloadItem>();
        }

        var technicianIds = technicians.Select(technician => technician.Id).ToList();
        var activeRepairOrders = FilterRepairOrders(context, activeOnly: true);

        var activeAssignments = await _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment =>
                assignment.UnassignedAtUtc == null
                && technicianIds.Contains(assignment.StaffMemberId))
            .Join(
                activeRepairOrders,
                assignment => assignment.RepairOrderId,
                repairOrder => repairOrder.Id,
                (assignment, repairOrder) => new
                {
                    assignment.StaffMemberId,
                    assignment.WorkStatus,
                    repairOrder.WorkshopLocationId,
                })
            .ToListAsync(cancellationToken);

        var workCompletedInPeriod = await _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment =>
                assignment.WorkCompletedAtUtc.HasValue
                && assignment.WorkCompletedAtUtc >= context.StartUtc
                && assignment.WorkCompletedAtUtc < context.EndUtcExclusive
                && technicianIds.Contains(assignment.StaffMemberId))
            .Join(
                FilterRepairOrders(context),
                assignment => assignment.RepairOrderId,
                repairOrder => repairOrder.Id,
                (assignment, _) => assignment.StaffMemberId)
            .GroupBy(staffMemberId => staffMemberId)
            .Select(group => new { StaffMemberId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var locationNames = await _dbContext.WorkshopLocations.AsNoTracking()
            .Select(location => new { location.Id, location.Name })
            .ToDictionaryAsync(location => location.Id, location => location.Name, cancellationToken);

        return technicians
            .Select(technician =>
            {
                var assignments = activeAssignments.Where(item => item.StaffMemberId == technician.Id).ToList();
                var primaryLocationId = assignments
                    .Select(item => item.WorkshopLocationId)
                    .FirstOrDefault();
                var locationName = primaryLocationId != Guid.Empty
                    ? locationNames.GetValueOrDefault(primaryLocationId)
                    : null;

                return new TechnicianWorkloadItem
                {
                    StaffMemberId = technician.Id,
                    DisplayName = technician.DisplayName,
                    WorkshopLocationName = locationName,
                    ActiveAssignedJobs = assignments.Count,
                    InProgressJobs = assignments.Count(item => item.WorkStatus == TechnicianWorkStatus.InProgress),
                    WorkCompletedAssignmentsInPeriod = workCompletedInPeriod
                        .SingleOrDefault(item => item.StaffMemberId == technician.Id)?.Count ?? 0,
                };
            })
            .OrderBy(item => item.DisplayName)
            .ToList();
    }

    private async Task<IReadOnlyList<LocationOperationalSummary>> BuildLocationBreakdownAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.WorkshopLocationId.HasValue)
        {
            return Array.Empty<LocationOperationalSummary>();
        }

        var locations = await _dbContext.WorkshopLocations.AsNoTracking()
            .OrderBy(location => location.Name)
            .Select(location => new { location.Id, location.Name })
            .ToListAsync(cancellationToken);

        var activeAssignmentRepairOrderIds = _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment => assignment.UnassignedAtUtc == null)
            .Select(assignment => assignment.RepairOrderId);

        var summaries = new List<LocationOperationalSummary>();
        foreach (var location in locations)
        {
            var locationContext = context with { WorkshopLocationId = location.Id };

            var activeRepairOrders = FilterRepairOrders(locationContext, activeOnly: true);
            var activeCount = await activeRepairOrders.CountAsync(cancellationToken);
            var unassigned = await activeRepairOrders
                .Where(repairOrder => !activeAssignmentRepairOrderIds.Contains(repairOrder.Id))
                .CountAsync(cancellationToken);
            var urgent = await activeRepairOrders
                .Where(repairOrder => repairOrder.Priority == RepairOrderPriority.Urgent)
                .CountAsync(cancellationToken);

            var appointmentsInPeriod = await FilterAppointments(locationContext)
                .Where(appointment =>
                    appointment.ScheduledStartUtc >= context.StartUtc
                    && appointment.ScheduledStartUtc < context.EndUtcExclusive)
                .CountAsync(cancellationToken);

            var completedJobs = await FilterRepairOrders(locationContext)
                .Where(repairOrder =>
                    repairOrder.CompletedAtUtc.HasValue
                    && repairOrder.CompletedAtUtc >= context.StartUtc
                    && repairOrder.CompletedAtUtc < context.EndUtcExclusive)
                .CountAsync(cancellationToken);

            var openDvi = await FilterInspectionsViaRepairOrder(locationContext)
                .Where(inspection =>
                    inspection.Status == InspectionStatus.Draft
                    || inspection.Status == InspectionStatus.InProgress)
                .CountAsync(cancellationToken);

            summaries.Add(new LocationOperationalSummary
            {
                WorkshopLocationId = location.Id,
                Name = location.Name,
                ActiveRepairOrders = activeCount,
                UnassignedRepairOrders = unassigned,
                UrgentRepairOrders = urgent,
                AppointmentsInPeriod = appointmentsInPeriod,
                CompletedJobsInPeriod = completedJobs,
                OpenDviInspections = openDvi,
            });
        }

        return summaries;
    }

    private async Task<InvoicePaymentStateBreakdown> BuildCurrentInvoicePaymentStateBreakdownAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var issuedInvoices = await LoadCurrentIssuedInvoicesAsync(context, cancellationToken);
        var financials = await LoadInvoiceFinancialsAsync(issuedInvoices, cancellationToken);

        var unpaid = 0;
        var partiallyPaid = 0;
        var paid = 0;
        foreach (var invoice in issuedInvoices)
        {
            var total = financials.Totals.GetValueOrDefault(invoice.Id, 0m);
            var amountPaid = financials.Paid.GetValueOrDefault(invoice.Id, 0m);
            var state = InvoiceMoneyCalculator.DerivePaymentState(total, amountPaid);
            switch (state)
            {
                case InvoicePaymentState.Unpaid:
                    unpaid++;
                    break;
                case InvoicePaymentState.PartiallyPaid:
                    partiallyPaid++;
                    break;
                case InvoicePaymentState.Paid:
                    paid++;
                    break;
            }
        }

        return new InvoicePaymentStateBreakdown
        {
            Unpaid = unpaid,
            PartiallyPaid = partiallyPaid,
            Paid = paid,
        };
    }

    private async Task<EstimateDecisionSourceBreakdown> BuildDecisionSourceBreakdownAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var portalApprovals = await _dbContext.EstimateShares.AsNoTracking()
            .Where(share =>
                share.Decision == EstimateShareDecision.Approved
                && share.DecisionAtUtc.HasValue
                && share.DecisionAtUtc >= context.StartUtc
                && share.DecisionAtUtc < context.EndUtcExclusive)
            .Join(
                FilterEstimatesViaRepairOrder(context),
                share => share.EstimateId,
                estimate => estimate.Id,
                (_, _) => 1)
            .CountAsync(cancellationToken);

        var portalDeclines = await _dbContext.EstimateShares.AsNoTracking()
            .Where(share =>
                share.Decision == EstimateShareDecision.Declined
                && share.DecisionAtUtc.HasValue
                && share.DecisionAtUtc >= context.StartUtc
                && share.DecisionAtUtc < context.EndUtcExclusive)
            .Join(
                FilterEstimatesViaRepairOrder(context),
                share => share.EstimateId,
                estimate => estimate.Id,
                (_, _) => 1)
            .CountAsync(cancellationToken);

        var approvedInPeriod = await FilterEstimatesViaRepairOrder(context)
            .Where(estimate =>
                estimate.ApprovedAtUtc.HasValue
                && estimate.ApprovedAtUtc >= context.StartUtc
                && estimate.ApprovedAtUtc < context.EndUtcExclusive)
            .Select(estimate => new { estimate.Id, estimate.ApprovedAtUtc })
            .ToListAsync(cancellationToken);

        var declinedInPeriod = await FilterEstimatesViaRepairOrder(context)
            .Where(estimate =>
                estimate.DeclinedAtUtc.HasValue
                && estimate.DeclinedAtUtc >= context.StartUtc
                && estimate.DeclinedAtUtc < context.EndUtcExclusive)
            .Select(estimate => new { estimate.Id, estimate.DeclinedAtUtc })
            .ToListAsync(cancellationToken);

        var portalApprovedEstimateIds = await _dbContext.EstimateShares.AsNoTracking()
            .Where(share => share.Decision == EstimateShareDecision.Approved)
            .Select(share => share.EstimateId)
            .ToListAsync(cancellationToken);

        var portalDeclinedEstimateIds = await _dbContext.EstimateShares.AsNoTracking()
            .Where(share => share.Decision == EstimateShareDecision.Declined)
            .Select(share => share.EstimateId)
            .ToListAsync(cancellationToken);

        var staffApprovals = approvedInPeriod.Count(item => !portalApprovedEstimateIds.Contains(item.Id));
        var staffDeclines = declinedInPeriod.Count(item => !portalDeclinedEstimateIds.Contains(item.Id));

        return new EstimateDecisionSourceBreakdown
        {
            PortalApprovalsInPeriod = portalApprovals,
            PortalDeclinesInPeriod = portalDeclines,
            StaffApprovalsInPeriod = staffApprovals,
            StaffDeclinesInPeriod = staffDeclines,
        };
    }

    private async Task<int> CountCommerciallyClosableJobsAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var completedRepairOrders = await FilterRepairOrders(context)
            .Where(repairOrder =>
                repairOrder.Status == RepairOrderStatus.Completed
                && repairOrder.CommerciallyClosedAtUtc == null)
            .Select(repairOrder => repairOrder.Id)
            .ToListAsync(cancellationToken);

        if (completedRepairOrders.Count == 0)
        {
            return 0;
        }

        var invoices = await _dbContext.Invoices.AsNoTracking()
            .Where(invoice =>
                completedRepairOrders.Contains(invoice.RepairOrderId)
                && invoice.Status == InvoiceStatus.Issued
                && invoice.VoidedAtUtc == null)
            .Select(invoice => new InvoiceRow(invoice.Id, invoice.CurrencyCode, invoice.RepairOrderId))
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return 0;
        }

        var financials = await LoadInvoiceFinancialsAsync(invoices, cancellationToken);
        var closableRepairOrderIds = new HashSet<Guid>();
        foreach (var invoice in invoices)
        {
            var total = financials.Totals.GetValueOrDefault(invoice.Id, 0m);
            var paid = financials.Paid.GetValueOrDefault(invoice.Id, 0m);
            if (InvoiceMoneyCalculator.DerivePaymentState(total, paid) == InvoicePaymentState.Paid)
            {
                closableRepairOrderIds.Add(invoice.RepairOrderId);
            }
        }

        return closableRepairOrderIds.Count;
    }

    private async Task<List<InvoiceRow>> LoadCurrentIssuedInvoicesAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        return await FilterInvoicesViaRepairOrder(context)
            .Where(invoice => invoice.Status == InvoiceStatus.Issued && invoice.VoidedAtUtc == null)
            .Select(invoice => new InvoiceRow(invoice.Id, invoice.CurrencyCode, invoice.RepairOrderId))
            .ToListAsync(cancellationToken);
    }

    private async Task<InvoiceFinancialSnapshot> LoadInvoiceFinancialsAsync(
        IReadOnlyList<InvoiceRow> invoices,
        CancellationToken cancellationToken)
    {
        if (invoices.Count == 0)
        {
            return InvoiceFinancialSnapshot.Empty;
        }

        var invoiceIds = invoices.Select(invoice => invoice.Id).ToList();

        var items = await _dbContext.InvoiceItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.InvoiceId))
            .Select(item => new
            {
                item.InvoiceId,
                item.Quantity,
                item.UnitPrice,
            })
            .ToListAsync(cancellationToken);

        var totals = items
            .GroupBy(item => item.InvoiceId)
            .ToDictionary(
                group => group.Key,
                group => InvoiceMoneyCalculator.CalculateInvoiceTotal(
                    group.Select(item => (item.Quantity, item.UnitPrice)).ToList()));

        var paid = await _dbContext.InvoicePaymentRecords.AsNoTracking()
            .Where(record => invoiceIds.Contains(record.InvoiceId))
            .GroupBy(record => record.InvoiceId)
            .Select(group => new
            {
                InvoiceId = group.Key,
                AmountPaid = group.Sum(record => record.Amount),
            })
            .ToDictionaryAsync(item => item.InvoiceId, item => item.AmountPaid, cancellationToken);

        return new InvoiceFinancialSnapshot(totals, paid);
    }

    private async Task<IReadOnlyList<MoneyBreakdownItem>> SumInvoicedAmountsInPeriodAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var invoices = await FilterInvoicesViaRepairOrder(context)
            .Where(invoice =>
                invoice.Status == InvoiceStatus.Issued
                && invoice.IssuedAtUtc.HasValue
                && invoice.IssuedAtUtc >= context.StartUtc
                && invoice.IssuedAtUtc < context.EndUtcExclusive)
            .Select(invoice => new InvoiceRow(invoice.Id, invoice.CurrencyCode, invoice.RepairOrderId))
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return Array.Empty<MoneyBreakdownItem>();
        }

        var financials = await LoadInvoiceFinancialsAsync(invoices, cancellationToken);
        var amountsByCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoices)
        {
            var total = financials.Totals.GetValueOrDefault(invoice.Id, 0m);
            amountsByCurrency[invoice.CurrencyCode] =
                amountsByCurrency.GetValueOrDefault(invoice.CurrencyCode, 0m) + total;
        }

        return ToMoneyBreakdown(amountsByCurrency);
    }

    private async Task<IReadOnlyList<MoneyBreakdownItem>> SumRecordedPaymentsInPeriodAsync(
        ReportingExecutionContext context,
        CancellationToken cancellationToken)
    {
        var amountsByCurrency = await FilterInvoicesViaRepairOrder(context)
            .Join(
                _dbContext.InvoicePaymentRecords.AsNoTracking()
                    .Where(record =>
                        record.RecordedAtUtc >= context.StartUtc
                        && record.RecordedAtUtc < context.EndUtcExclusive),
                invoice => invoice.Id,
                record => record.InvoiceId,
                (invoice, record) => new { invoice.CurrencyCode, record.Amount })
            .GroupBy(item => item.CurrencyCode)
            .Select(group => new MoneyBreakdownItem
            {
                CurrencyCode = group.Key,
                Amount = group.Sum(item => item.Amount),
            })
            .ToListAsync(cancellationToken);

        return amountsByCurrency
            .OrderBy(item => item.CurrencyCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IQueryable<RepairOrder> FilterRepairOrders(
        ReportingExecutionContext context,
        bool activeOnly = false)
    {
        var query = _dbContext.RepairOrders.AsNoTracking();
        if (context.WorkshopLocationId.HasValue)
        {
            query = query.Where(repairOrder => repairOrder.WorkshopLocationId == context.WorkshopLocationId.Value);
        }

        if (activeOnly)
        {
            query = query.Where(repairOrder =>
                repairOrder.Status != RepairOrderStatus.Completed
                && repairOrder.Status != RepairOrderStatus.Cancelled);
        }

        return query;
    }

    private IQueryable<Appointment> FilterAppointments(ReportingExecutionContext context)
    {
        var query = _dbContext.Appointments.AsNoTracking();
        if (context.WorkshopLocationId.HasValue)
        {
            query = query.Where(appointment => appointment.WorkshopLocationId == context.WorkshopLocationId.Value);
        }

        return query;
    }

    private IQueryable<Inspection> FilterInspectionsViaRepairOrder(ReportingExecutionContext context)
    {
        var repairOrders = FilterRepairOrders(context);
        return _dbContext.Inspections.AsNoTracking()
            .Join(
                repairOrders,
                inspection => inspection.RepairOrderId,
                repairOrder => repairOrder.Id,
                (inspection, _) => inspection);
    }

    private IQueryable<Estimate> FilterEstimatesViaRepairOrder(ReportingExecutionContext context)
    {
        var repairOrders = FilterRepairOrders(context);
        return _dbContext.Estimates.AsNoTracking()
            .Join(
                repairOrders,
                estimate => estimate.RepairOrderId,
                repairOrder => repairOrder.Id,
                (estimate, _) => estimate);
    }

    private IQueryable<Invoice> FilterInvoicesViaRepairOrder(ReportingExecutionContext context)
    {
        var repairOrders = FilterRepairOrders(context);
        return _dbContext.Invoices.AsNoTracking()
            .Join(
                repairOrders,
                invoice => invoice.RepairOrderId,
                repairOrder => repairOrder.Id,
                (invoice, _) => invoice);
    }

    private async Task<bool> CanActorViewReportingAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return false;
        }

        var membershipRole = await (
            from membership in _dbContext.OrganizationMemberships.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.UserId == actorUserId
                  && membership.OrganizationId == organizationId
                  && membership.Status == OrganizationMembershipStatus.Active
                  && organization.Status == OrganizationStatus.Active
            select membership.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return ReportingViewerPolicy.CanViewReporting(membershipRole);
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (!_organizationContext.IsResolved || !_organizationContext.OrganizationId.HasValue)
        {
            organizationId = default;
            return false;
        }

        organizationId = _organizationContext.OrganizationId.Value;
        return true;
    }

    private static AppointmentStatusBreakdown BuildAppointmentStatusBreakdown(
        IEnumerable<(AppointmentStatus Status, int Count)> rows)
    {
        int scheduled = 0;
        int confirmed = 0;
        int checkedIn = 0;
        int completed = 0;
        int cancelled = 0;
        int noShow = 0;

        foreach (var (status, count) in rows)
        {
            switch (status)
            {
                case AppointmentStatus.Scheduled:
                    scheduled = count;
                    break;
                case AppointmentStatus.Confirmed:
                    confirmed = count;
                    break;
                case AppointmentStatus.CheckedIn:
                    checkedIn = count;
                    break;
                case AppointmentStatus.Completed:
                    completed = count;
                    break;
                case AppointmentStatus.Cancelled:
                    cancelled = count;
                    break;
                case AppointmentStatus.NoShow:
                    noShow = count;
                    break;
            }
        }

        return new AppointmentStatusBreakdown
        {
            Scheduled = scheduled,
            Confirmed = confirmed,
            CheckedIn = checkedIn,
            Completed = completed,
            Cancelled = cancelled,
            NoShow = noShow,
        };
    }

    private static IReadOnlyList<MoneyBreakdownItem> ToMoneyBreakdown(
        Dictionary<string, decimal> amountsByCurrency) =>
        amountsByCurrency
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new MoneyBreakdownItem
            {
                CurrencyCode = pair.Key,
                Amount = pair.Value,
            })
            .ToList();

    private sealed record ReportingExecutionContext(
        Guid OrganizationId,
        Guid? WorkshopLocationId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtcExclusive,
        ReportingContextHeader Header);

    private readonly record struct InvoiceRow(Guid Id, string CurrencyCode, Guid RepairOrderId);

    private sealed class InvoiceFinancialSnapshot
    {
        public static InvoiceFinancialSnapshot Empty { get; } =
            new(new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>());

        public InvoiceFinancialSnapshot(
            Dictionary<Guid, decimal> totals,
            Dictionary<Guid, decimal> paid)
        {
            Totals = totals;
            Paid = paid;
        }

        public Dictionary<Guid, decimal> Totals { get; }

        public Dictionary<Guid, decimal> Paid { get; }
    }
}
