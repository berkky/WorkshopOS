using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Billing;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Billing;

public sealed class PaymentManagementService : IPaymentManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public PaymentManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<PaymentOperationResult<Guid>> RecordPaymentAsync(
        Guid actorUserId,
        RecordPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.Unauthorized);
        }

        if (!InvoiceInputValidator.IsValidPaymentAmount(command.Amount)
            || !InvoiceInputValidator.TryNormalizeReference(command.Reference, out var reference)
            || !InvoiceInputValidator.TryNormalizeNote(command.Note, out var note))
        {
            return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.InvalidInput);
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
            var invoice = await _dbContext.Invoices
                .SingleOrDefaultAsync(candidate => candidate.Id == command.InvoiceId, cancellationToken);

            if (invoice is null)
            {
                return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.InvoiceNotFound);
            }

            var repairOrder = await _dbContext.RepairOrders
                .SingleAsync(candidate => candidate.Id == invoice.RepairOrderId, cancellationToken);

            if (repairOrder.CommerciallyClosedAtUtc.HasValue)
            {
                return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.CommerciallyClosed);
            }

            if (!InvoiceLifecyclePolicy.CanRecordPayment(invoice.Status))
            {
                return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.InvalidLifecycleTransition);
            }

            var items = await _dbContext.InvoiceItems.AsNoTracking()
                .Where(candidate => candidate.InvoiceId == invoice.Id)
                .Select(candidate => new { candidate.Quantity, candidate.UnitPrice })
                .ToListAsync(cancellationToken);

            var total = InvoiceMoneyCalculator.CalculateInvoiceTotal(
                items.Select(item => (item.Quantity, item.UnitPrice)).ToList());

            var amountPaid = await _dbContext.InvoicePaymentRecords
                .Where(candidate => candidate.InvoiceId == invoice.Id)
                .SumAsync(candidate => candidate.Amount, cancellationToken);

            if (amountPaid + command.Amount > total)
            {
                return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.Overpayment);
            }

            var recordedAtUtc = _timeProvider.GetUtcNow();
            var payment = new InvoicePaymentRecord(
                organizationId,
                invoice.Id,
                command.Amount,
                command.PaymentMethod,
                actorUserId,
                recordedAtUtc,
                reference,
                note);

            _dbContext.InvoicePaymentRecords.Add(payment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return PaymentOperationResult<Guid>.Succeeded(payment.Id);
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return PaymentOperationResult<Guid>.Failed(PaymentOperationFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<InvoiceOperationResult> CloseRepairOrderCommerciallyAsync(
        Guid actorUserId,
        Guid repairOrderId,
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
            var repairOrder = await _dbContext.RepairOrders
                .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

            if (repairOrder is null)
            {
                return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.RepairOrderNotFound);
            }

            if (repairOrder.CommerciallyClosedAtUtc.HasValue)
            {
                return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.AlreadyCommerciallyClosed);
            }

            if (repairOrder.Status != RepairOrderStatus.Completed)
            {
                return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.RepairOrderNotCompleted);
            }

            var currentInvoice = await _dbContext.Invoices
                .Where(candidate => candidate.RepairOrderId == repairOrderId && candidate.VoidedAtUtc == null)
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentInvoice is null || currentInvoice.Status != InvoiceStatus.Issued)
            {
                return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvoiceNotPaid);
            }

            var items = await _dbContext.InvoiceItems.AsNoTracking()
                .Where(candidate => candidate.InvoiceId == currentInvoice.Id)
                .Select(candidate => new { candidate.Quantity, candidate.UnitPrice })
                .ToListAsync(cancellationToken);

            var total = InvoiceMoneyCalculator.CalculateInvoiceTotal(
                items.Select(item => (item.Quantity, item.UnitPrice)).ToList());

            var amountPaid = await _dbContext.InvoicePaymentRecords
                .Where(candidate => candidate.InvoiceId == currentInvoice.Id)
                .SumAsync(candidate => candidate.Amount, cancellationToken);

            if (InvoiceMoneyCalculator.DerivePaymentState(total, amountPaid) != InvoicePaymentState.Paid)
            {
                return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.InvoiceNotPaid);
            }

            repairOrder.CloseCommercially(_timeProvider.GetUtcNow());
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return InvoiceOperationResult.Succeeded();
        }
        catch (InvalidOperationException)
        {
            return InvoiceOperationResult.Failed(InvoiceOperationFailureReason.AlreadyCommerciallyClosed);
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
}
