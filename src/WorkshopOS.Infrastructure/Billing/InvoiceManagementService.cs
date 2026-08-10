using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Billing;

public sealed class InvoiceManagementService : IInvoiceManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly IInvoiceNumberGenerator _numberGenerator;
    private readonly TimeProvider _timeProvider;

    public InvoiceManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        IInvoiceNumberGenerator numberGenerator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _numberGenerator = numberGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<InvoiceListResult> ListInvoicesAsync(
        InvoiceListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => InvoiceListQuery.DefaultPageSize,
            > InvoiceListQuery.MaxPageSize => InvoiceListQuery.MaxPageSize,
            _ => query.PageSize,
        };

        var invoices = ApplyFilters(BuildInvoiceProjection(), query);

        if (query.PaymentState.HasValue)
        {
            var candidateIds = await invoices
                .Select(row => row.Invoice.Id)
                .ToListAsync(cancellationToken);

            var filteredIds = await FilterInvoiceIdsByPaymentStateAsync(
                candidateIds,
                query.PaymentState.Value,
                cancellationToken);

            invoices = invoices.Where(row => filteredIds.Contains(row.Invoice.Id));
        }

        var totalCount = await invoices.CountAsync(cancellationToken);

        var rows = await invoices
            .OrderByDescending(row => row.Invoice.CreatedAtUtc)
            .ThenByDescending(row => row.Invoice.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var invoiceIds = rows.Select(row => row.Invoice.Id).ToList();
        var totalsByInvoice = await LoadTotalsByInvoiceAsync(invoiceIds, cancellationToken);
        var paidByInvoice = await LoadAmountPaidByInvoiceAsync(invoiceIds, cancellationToken);

        var items = rows
            .Select(row =>
            {
                var total = totalsByInvoice.GetValueOrDefault(row.Invoice.Id, 0m);
                var amountPaid = paidByInvoice.GetValueOrDefault(row.Invoice.Id, 0m);

                return new InvoiceListItem
                {
                    InvoiceId = row.Invoice.Id,
                    Number = row.Invoice.Number,
                    Status = row.Invoice.Status,
                    PaymentState = InvoiceMoneyCalculator.DerivePaymentState(total, amountPaid),
                    CurrencyCode = row.Invoice.CurrencyCode,
                    Total = total,
                    AmountPaid = amountPaid,
                    RepairOrderId = row.RepairOrder.Id,
                    RepairOrderNumber = row.RepairOrder.Number,
                    CustomerDisplayName = row.Customer.DisplayName,
                    VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
                    WorkshopLocationName = row.Location.Name,
                    CreatedAtUtc = row.Invoice.CreatedAtUtc,
                    IssuedAtUtc = row.Invoice.IssuedAtUtc,
                };
            })
            .ToList();

        return new InvoiceListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<InvoiceDetails?> GetInvoiceDetailsAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var row = await BuildInvoiceProjection()
            .SingleOrDefaultAsync(candidate => candidate.Invoice.Id == invoiceId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var items = await _dbContext.InvoiceItems.AsNoTracking()
            .Where(candidate => candidate.InvoiceId == invoiceId)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var payments = await _dbContext.InvoicePaymentRecords.AsNoTracking()
            .Where(candidate => candidate.InvoiceId == invoiceId)
            .OrderBy(candidate => candidate.RecordedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        string? sourceEstimateNumber = null;
        if (row.Invoice.SourceEstimateId.HasValue)
        {
            sourceEstimateNumber = await _dbContext.Estimates.AsNoTracking()
                .Where(candidate => candidate.Id == row.Invoice.SourceEstimateId.Value)
                .Select(candidate => candidate.Number)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return MapDetails(row, items, payments, sourceEstimateNumber);
    }

    public async Task<RepairOrderBillingSummary?> GetRepairOrderBillingSummaryAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var repairOrder = await _dbContext.RepairOrders.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return null;
        }

        var currentInvoice = await _dbContext.Invoices.AsNoTracking()
            .Where(candidate => candidate.RepairOrderId == repairOrderId && candidate.VoidedAtUtc == null)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentInvoice is null)
        {
            return new RepairOrderBillingSummary
            {
                CommerciallyClosed = repairOrder.CommerciallyClosedAtUtc.HasValue,
                CanCreateInvoice = CanCreateInvoiceForRepairOrder(repairOrder),
                CanCloseCommercially = false,
            };
        }

        var totalsByInvoice = await LoadTotalsByInvoiceAsync([currentInvoice.Id], cancellationToken);
        var paidByInvoice = await LoadAmountPaidByInvoiceAsync([currentInvoice.Id], cancellationToken);
        var total = totalsByInvoice.GetValueOrDefault(currentInvoice.Id, 0m);
        var amountPaid = paidByInvoice.GetValueOrDefault(currentInvoice.Id, 0m);
        var paymentState = InvoiceMoneyCalculator.DerivePaymentState(total, amountPaid);

        return new RepairOrderBillingSummary
        {
            CurrentInvoiceId = currentInvoice.Id,
            CurrentInvoiceNumber = currentInvoice.Number,
            CurrentInvoiceStatus = currentInvoice.Status,
            PaymentState = paymentState,
            Total = total,
            AmountPaid = amountPaid,
            RemainingBalance = Math.Max(0m, total - amountPaid),
            CommerciallyClosed = repairOrder.CommerciallyClosedAtUtc.HasValue,
            CanCreateInvoice = false,
            CanCloseCommercially = CanCloseCommercially(repairOrder, currentInvoice, paymentState),
        };
    }

    public async Task<InvoiceOperationResult<Guid>> CreateInvoiceAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.RepairOrderNotFound);
        }

        var eligibilityFailure = ValidateRepairOrderForInvoiceMutation(repairOrder);
        if (eligibilityFailure.HasValue)
        {
            return InvoiceOperationResult<Guid>.Failed(eligibilityFailure.Value);
        }

        if (await HasActiveInvoiceForRepairOrderAsync(repairOrderId, cancellationToken))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.CurrentInvoiceExists);
        }

        var organization = await _dbContext.Organizations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == organizationId, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var invoice = new Invoice(
            organizationId,
            repairOrderId,
            _numberGenerator.Generate(now),
            organization.DefaultCurrencyCode);

        _dbContext.Invoices.Add(invoice);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.CurrentInvoiceExists);
        }

        return InvoiceOperationResult<Guid>.Succeeded(invoice.Id);
    }

    public async Task<InvoiceOperationResult<Guid>> CreateInvoiceFromApprovedEstimateAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.EstimateNotFound);
        }

        if (estimate.Status != EstimateStatus.Approved)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.EstimateNotEligible);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == estimate.RepairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.RepairOrderNotFound);
        }

        var eligibilityFailure = ValidateRepairOrderForInvoiceMutation(repairOrder);
        if (eligibilityFailure.HasValue)
        {
            return InvoiceOperationResult<Guid>.Failed(eligibilityFailure.Value);
        }

        if (await HasActiveInvoiceForRepairOrderAsync(repairOrder.Id, cancellationToken))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.CurrentInvoiceExists);
        }

        if (await HasActiveInvoiceForSourceEstimateAsync(estimateId, cancellationToken))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.SourceEstimateInvoiceExists);
        }

        var estimateItems = await _dbContext.EstimateItems.AsNoTracking()
            .Where(candidate => candidate.EstimateId == estimateId)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var invoice = new Invoice(
            organizationId,
            repairOrder.Id,
            _numberGenerator.Generate(now),
            estimate.CurrencyCode,
            estimateId);

        _dbContext.Invoices.Add(invoice);

        foreach (var estimateItem in estimateItems)
        {
            var invoiceItem = new InvoiceItem(
                organizationId,
                invoice.Id,
                InvoiceItemTypeMapper.FromEstimateItemType(estimateItem.Type),
                estimateItem.Description,
                estimateItem.Quantity,
                estimateItem.UnitPrice,
                estimateItem.SortOrder);

            _dbContext.InvoiceItems.Add(invoiceItem);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            if (await HasActiveInvoiceForSourceEstimateAsync(estimateId, cancellationToken))
            {
                return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.SourceEstimateInvoiceExists);
            }

            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.CurrentInvoiceExists);
        }

        return InvoiceOperationResult<Guid>.Succeeded(invoice.Id);
    }

    public async Task<InvoiceOperationResult<Guid>> AddInvoiceItemAsync(
        Guid actorUserId,
        AddInvoiceItemCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        if (!InvoiceInputValidator.TryNormalizeDescription(command.Description, out var description)
            || !InvoiceInputValidator.IsValidQuantity(command.Quantity)
            || !InvoiceInputValidator.IsValidUnitPrice(command.UnitPrice))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.InvalidInput);
        }

        var invoice = await _dbContext.Invoices
            .SingleOrDefaultAsync(candidate => candidate.Id == command.InvoiceId, cancellationToken);

        if (invoice is null)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.InvoiceNotFound);
        }

        var repairOrder = await _dbContext.RepairOrders
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.RepairOrderId, cancellationToken);

        if (repairOrder.CommerciallyClosedAtUtc.HasValue)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.CommerciallyClosed);
        }

        if (!InvoiceLifecyclePolicy.IsFinanciallyMutable(invoice.Status))
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
        }

        var itemCount = await _dbContext.InvoiceItems
            .CountAsync(candidate => candidate.InvoiceId == invoice.Id, cancellationToken);

        if (itemCount >= InvoiceInputValidator.MaxItemsPerInvoice)
        {
            return InvoiceOperationResult<Guid>.Failed(InvoiceOperationFailureReason.ItemLimitExceeded);
        }

        var maxSortOrder = await _dbContext.InvoiceItems
            .Where(candidate => candidate.InvoiceId == invoice.Id)
            .Select(candidate => (int?)candidate.SortOrder)
            .MaxAsync(cancellationToken);

        var sortOrder = (maxSortOrder ?? 0) + 10;

        var item = new InvoiceItem(
            organizationId,
            invoice.Id,
            command.Type,
            description,
            command.Quantity,
            command.UnitPrice,
            sortOrder);

        _dbContext.InvoiceItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return InvoiceOperationResult<Guid>.Succeeded(item.Id);
    }

    public async Task<InvoiceOperationResult> UpdateInvoiceItemAsync(
        Guid actorUserId,
        UpdateInvoiceItemCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        if (!InvoiceInputValidator.TryNormalizeDescription(command.Description, out var description)
            || !InvoiceInputValidator.IsValidQuantity(command.Quantity)
            || !InvoiceInputValidator.IsValidUnitPrice(command.UnitPrice))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidInput);
        }

        var invoice = await _dbContext.Invoices
            .SingleOrDefaultAsync(candidate => candidate.Id == command.InvoiceId, cancellationToken);

        if (invoice is null)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvoiceNotFound);
        }

        var repairOrder = await _dbContext.RepairOrders
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.RepairOrderId, cancellationToken);

        if (repairOrder.CommerciallyClosedAtUtc.HasValue)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.CommerciallyClosed);
        }

        if (!InvoiceLifecyclePolicy.IsFinanciallyMutable(invoice.Status))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
        }

        var item = await _dbContext.InvoiceItems
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.InvoiceItemId && candidate.InvoiceId == command.InvoiceId,
                cancellationToken);

        if (item is null)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.ItemNotFound);
        }

        item.Update(command.Type, description, command.Quantity, command.UnitPrice, item.SortOrder);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return InvoiceOperationResult.Succeeded();
    }

    public async Task<InvoiceOperationResult> RemoveInvoiceItemAsync(
        Guid actorUserId,
        Guid invoiceId,
        Guid invoiceItemId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        var invoice = await _dbContext.Invoices
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvoiceNotFound);
        }

        var repairOrder = await _dbContext.RepairOrders
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.RepairOrderId, cancellationToken);

        if (repairOrder.CommerciallyClosedAtUtc.HasValue)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.CommerciallyClosed);
        }

        if (!InvoiceLifecyclePolicy.IsFinanciallyMutable(invoice.Status))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
        }

        var item = await _dbContext.InvoiceItems
            .SingleOrDefaultAsync(
                candidate => candidate.Id == invoiceItemId && candidate.InvoiceId == invoiceId,
                cancellationToken);

        if (item is null)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.ItemNotFound);
        }

        _dbContext.InvoiceItems.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return InvoiceOperationResult.Succeeded();
    }

    public async Task<InvoiceOperationResult> UpdateCommercialNotesAsync(
        Guid actorUserId,
        Guid invoiceId,
        string? commercialNotes,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        if (!InvoiceInputValidator.TryNormalizeCommercialNotes(commercialNotes, out var normalized))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidInput);
        }

        var invoice = await _dbContext.Invoices
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvoiceNotFound);
        }

        var repairOrder = await _dbContext.RepairOrders
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.RepairOrderId, cancellationToken);

        if (repairOrder.CommerciallyClosedAtUtc.HasValue)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.CommerciallyClosed);
        }

        if (!InvoiceLifecyclePolicy.IsFinanciallyMutable(invoice.Status))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
        }

        invoice.UpdateCommercialNotes(normalized);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return InvoiceOperationResult.Succeeded();
    }

    public Task<InvoiceOperationResult> IssueInvoiceAsync(
        Guid actorUserId,
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        ExecuteLifecycleMutationAsync(
            actorUserId,
            invoiceId,
            cancellationToken,
            async invoice =>
            {
                if (!InvoiceLifecyclePolicy.CanIssue(invoice.Status))
                {
                    return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
                }

                var itemCount = await _dbContext.InvoiceItems
                    .CountAsync(candidate => candidate.InvoiceId == invoice.Id, cancellationToken);

                if (itemCount == 0)
                {
                    return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.EmptyInvoice);
                }

                invoice.Issue(_timeProvider.GetUtcNow());
                return InvoiceOperationResult.Succeeded();
            });

    public Task<InvoiceOperationResult> VoidInvoiceAsync(
        Guid actorUserId,
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        ExecuteLifecycleMutationAsync(
            actorUserId,
            invoiceId,
            cancellationToken,
            async invoice =>
            {
                if (!InvoiceLifecyclePolicy.CanVoid(invoice.Status))
                {
                    return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
                }

                if (invoice.Status == InvoiceStatus.Issued)
                {
                    var paymentCount = await _dbContext.InvoicePaymentRecords
                        .CountAsync(candidate => candidate.InvoiceId == invoice.Id, cancellationToken);

                    if (paymentCount > 0)
                    {
                        return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.HasPayments);
                    }
                }

                invoice.Void(_timeProvider.GetUtcNow());
                return InvoiceOperationResult.Succeeded();
            });

    private async Task<InvoiceOperationResult> ExecuteLifecycleMutationAsync(
        Guid actorUserId,
        Guid invoiceId,
        CancellationToken cancellationToken,
        Func<Invoice, Task<InvoiceOperationResult>> mutate)
    {
        if (!TryGetOrganizationId(out _))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.Unauthorized);
        }

        var invoice = await _dbContext.Invoices
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvoiceNotFound);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleAsync(candidate => candidate.Id == invoice.RepairOrderId, cancellationToken);

        if (repairOrder.CommerciallyClosedAtUtc.HasValue)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.CommerciallyClosed);
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        }

        try
        {
            var result = await mutate(invoice);
            if (!result.Success)
            {
                return result;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return InvoiceOperationResult.Succeeded();
        }
        catch (InvalidOperationException)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvalidLifecycleTransition);
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static InvoiceDetails MapDetails(
        InvoiceProjectionRow row,
        IReadOnlyList<InvoiceItem> items,
        IReadOnlyList<InvoicePaymentRecord> payments,
        string? sourceEstimateNumber)
    {
        var itemDetails = items
            .Select(item => new InvoiceItemDetails
            {
                InvoiceItemId = item.Id,
                Type = item.Type,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = InvoiceMoneyCalculator.CalculateLineTotal(item.Quantity, item.UnitPrice),
                SortOrder = item.SortOrder,
            })
            .ToList();

        var total = InvoiceMoneyCalculator.CalculateInvoiceTotal(
            items.Select(item => (item.Quantity, item.UnitPrice)).ToList());
        var amountPaid = payments.Sum(payment => payment.Amount);
        var paymentState = InvoiceMoneyCalculator.DerivePaymentState(total, amountPaid);
        var commerciallyClosed = row.RepairOrder.CommerciallyClosedAtUtc.HasValue;

        return new InvoiceDetails
        {
            InvoiceId = row.Invoice.Id,
            Number = row.Invoice.Number,
            Status = row.Invoice.Status,
            PaymentState = paymentState,
            RepairOrderId = row.RepairOrder.Id,
            RepairOrderNumber = row.RepairOrder.Number,
            CustomerDisplayName = row.Customer.DisplayName,
            VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            WorkshopLocationName = row.Location.Name,
            SourceEstimateId = row.Invoice.SourceEstimateId,
            SourceEstimateNumber = sourceEstimateNumber,
            CurrencyCode = row.Invoice.CurrencyCode,
            Total = total,
            AmountPaid = amountPaid,
            RemainingBalance = Math.Max(0m, total - amountPaid),
            CommercialNotes = row.Invoice.CommercialNotes,
            Items = itemDetails,
            Payments = payments
                .Select(payment => new PaymentRecordDetails
                {
                    PaymentRecordId = payment.Id,
                    Amount = payment.Amount,
                    PaymentMethod = payment.PaymentMethod,
                    Reference = payment.Reference,
                    Note = payment.Note,
                    RecordedAtUtc = payment.RecordedAtUtc,
                })
                .ToList(),
            CreatedAtUtc = row.Invoice.CreatedAtUtc,
            UpdatedAtUtc = row.Invoice.UpdatedAtUtc,
            IssuedAtUtc = row.Invoice.IssuedAtUtc,
            VoidedAtUtc = row.Invoice.VoidedAtUtc,
            RepairOrderCommerciallyClosed = commerciallyClosed,
            CanEditItems = !commerciallyClosed && InvoiceLifecyclePolicy.IsFinanciallyMutable(row.Invoice.Status),
            CanIssue = !commerciallyClosed
                        && InvoiceLifecyclePolicy.CanIssue(row.Invoice.Status)
                        && items.Count > 0,
            CanVoid = !commerciallyClosed && InvoiceLifecyclePolicy.CanVoid(row.Invoice.Status),
            CanRecordPayment = !commerciallyClosed
                               && InvoiceLifecyclePolicy.CanRecordPayment(row.Invoice.Status)
                               && paymentState != InvoicePaymentState.Paid,
        };
    }

    private static bool CanCreateInvoiceForRepairOrder(RepairOrder repairOrder) =>
        repairOrder.CommerciallyClosedAtUtc is null
        && repairOrder.Status != RepairOrderStatus.Cancelled;

    private static bool CanCloseCommercially(
        RepairOrder repairOrder,
        Invoice currentInvoice,
        InvoicePaymentState paymentState) =>
        repairOrder.CommerciallyClosedAtUtc is null
        && repairOrder.Status == RepairOrderStatus.Completed
        && currentInvoice.Status == InvoiceStatus.Issued
        && paymentState == InvoicePaymentState.Paid;

    private static InvoiceOperationFailureReason? ValidateRepairOrderForInvoiceMutation(RepairOrder repairOrder)
    {
        if (repairOrder.CommerciallyClosedAtUtc.HasValue)
        {
            return InvoiceOperationFailureReason.CommerciallyClosed;
        }

        if (repairOrder.Status == RepairOrderStatus.Cancelled)
        {
            return InvoiceOperationFailureReason.RepairOrderNotEligible;
        }

        return null;
    }

    private async Task<bool> HasActiveInvoiceForRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken) =>
        await _dbContext.Invoices.AsNoTracking()
            .AnyAsync(
                candidate => candidate.RepairOrderId == repairOrderId && candidate.VoidedAtUtc == null,
                cancellationToken);

    private async Task<bool> HasActiveInvoiceForSourceEstimateAsync(
        Guid estimateId,
        CancellationToken cancellationToken) =>
        await _dbContext.Invoices.AsNoTracking()
            .AnyAsync(
                candidate => candidate.SourceEstimateId == estimateId && candidate.VoidedAtUtc == null,
                cancellationToken);

    private async Task<List<Guid>> FilterInvoiceIdsByPaymentStateAsync(
        IReadOnlyList<Guid> invoiceIds,
        InvoicePaymentState paymentState,
        CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
        {
            return [];
        }

        var totals = await LoadTotalsByInvoiceAsync(invoiceIds, cancellationToken);
        var paid = await LoadAmountPaidByInvoiceAsync(invoiceIds, cancellationToken);

        return invoiceIds
            .Where(invoiceId =>
            {
                var total = totals.GetValueOrDefault(invoiceId, 0m);
                var amountPaid = paid.GetValueOrDefault(invoiceId, 0m);
                return InvoiceMoneyCalculator.DerivePaymentState(total, amountPaid) == paymentState;
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, decimal>> LoadTotalsByInvoiceAsync(
        IReadOnlyList<Guid> invoiceIds,
        CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var items = await _dbContext.InvoiceItems.AsNoTracking()
            .Where(candidate => invoiceIds.Contains(candidate.InvoiceId))
            .Select(candidate => new
            {
                candidate.InvoiceId,
                candidate.Quantity,
                candidate.UnitPrice,
            })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(item => item.InvoiceId)
            .ToDictionary(
                group => group.Key,
                group => InvoiceMoneyCalculator.CalculateInvoiceTotal(
                    group.Select(item => (item.Quantity, item.UnitPrice)).ToList()));
    }

    private async Task<Dictionary<Guid, decimal>> LoadAmountPaidByInvoiceAsync(
        IReadOnlyList<Guid> invoiceIds,
        CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        return await _dbContext.InvoicePaymentRecords.AsNoTracking()
            .Where(candidate => invoiceIds.Contains(candidate.InvoiceId))
            .GroupBy(candidate => candidate.InvoiceId)
            .Select(group => new
            {
                InvoiceId = group.Key,
                AmountPaid = group.Sum(candidate => candidate.Amount),
            })
            .ToDictionaryAsync(candidate => candidate.InvoiceId, candidate => candidate.AmountPaid, cancellationToken);
    }

    private async Task<bool> ValidateManagerAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var role = await ResolveMembershipRoleAsync(actorUserId, cancellationToken);
        return BillingManagerPolicy.CanManageBilling(role);
    }

    private async Task<OrganizationMembershipRole> ResolveMembershipRoleAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return default;
        }

        return await _dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == actorUserId
                                 && membership.OrganizationId == organizationId
                                 && membership.Status == OrganizationMembershipStatus.Active)
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<InvoiceProjectionRow> BuildInvoiceProjection()
    {
        return from invoice in _dbContext.Invoices.AsNoTracking()
               join repairOrder in _dbContext.RepairOrders.AsNoTracking()
                   on invoice.RepairOrderId equals repairOrder.Id
               join location in _dbContext.WorkshopLocations.AsNoTracking()
                   on repairOrder.WorkshopLocationId equals location.Id
               join customer in _dbContext.Customers.AsNoTracking()
                   on repairOrder.CustomerId equals customer.Id
               join vehicle in _dbContext.Vehicles.AsNoTracking()
                   on repairOrder.VehicleId equals vehicle.Id
               select new InvoiceProjectionRow
               {
                   Invoice = invoice,
                   RepairOrder = repairOrder,
                   Location = location,
                   Customer = customer,
                   Vehicle = vehicle,
               };
    }

    private static IQueryable<InvoiceProjectionRow> ApplyFilters(
        IQueryable<InvoiceProjectionRow> invoices,
        InvoiceListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            invoices = invoices.Where(row =>
                row.Invoice.Number.Contains(term)
                || row.RepairOrder.Number.Contains(term)
                || row.Customer.DisplayName.Contains(term)
                || row.Vehicle.Make.Contains(term)
                || row.Vehicle.Model.Contains(term)
                || (row.Vehicle.RegistrationPlate != null && row.Vehicle.RegistrationPlate.Contains(term)));
        }

        if (query.Status.HasValue)
        {
            invoices = invoices.Where(row => row.Invoice.Status == query.Status.Value);
        }

        if (query.RepairOrderId.HasValue)
        {
            invoices = invoices.Where(row => row.Invoice.RepairOrderId == query.RepairOrderId.Value);
        }

        if (query.WorkshopLocationId.HasValue)
        {
            invoices = invoices.Where(row => row.RepairOrder.WorkshopLocationId == query.WorkshopLocationId.Value);
        }

        return invoices;
    }

    private static bool IsSerializationConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException && postgresException.SqlState is "40001")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (_organizationContext.IsResolved && _organizationContext.OrganizationId.HasValue)
        {
            organizationId = _organizationContext.OrganizationId.Value;
            return true;
        }

        organizationId = Guid.Empty;
        return false;
    }

    private static InvoiceListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<InvoiceListItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = InvoiceListQuery.DefaultPageSize,
        };

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private sealed class InvoiceProjectionRow
    {
        public required Invoice Invoice { get; init; }

        public required RepairOrder RepairOrder { get; init; }

        public required WorkshopLocation Location { get; init; }

        public required Domain.Customers.Customer Customer { get; init; }

        public required Domain.Vehicles.Vehicle Vehicle { get; init; }
    }
}
