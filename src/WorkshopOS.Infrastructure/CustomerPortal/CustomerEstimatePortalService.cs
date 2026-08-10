using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.CustomerPortal;

public sealed class CustomerEstimatePortalService : ICustomerEstimatePortalService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContextMutator _organizationContextMutator;
    private readonly TimeProvider _timeProvider;

    public CustomerEstimatePortalService(
        AppDbContext dbContext,
        IOrganizationContextMutator organizationContextMutator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContextMutator = organizationContextMutator;
        _timeProvider = timeProvider;
    }

    public async Task<CustomerPortalSessionValidation?> ValidateSessionAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default)
    {
        var share = await FindActiveShareByPublicIdAsync(sharePublicId, cancellationToken);
        if (share is null)
        {
            return null;
        }

        return new CustomerPortalSessionValidation
        {
            SharePublicId = share.PublicId,
            OrganizationId = share.OrganizationId,
            EstimateId = share.EstimateId,
        };
    }

    public async Task<CustomerPortalOperationResult> ExchangeTokenAsync(
        ExchangeEstimateShareTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RawToken))
        {
            return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.InvalidAccess);
        }

        var share = await _dbContext.EstimateShares
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.PublicId == command.PublicId, cancellationToken);

        if (share is null || !IsShareAccessAllowed(share))
        {
            return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.InvalidAccess);
        }

        var presentedHash = EstimateShareTokenHasher.HashToHex(command.RawToken.Trim());
        if (!EstimateShareTokenHasher.FixedTimeEqualsHex(presentedHash, share.TokenHash))
        {
            return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.InvalidAccess);
        }

        var estimate = await _dbContext.Estimates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == share.EstimateId, cancellationToken);

        if (estimate is null
            || !EstimateShareEligibilityPolicy.IsCustomerVisible(estimate.Status))
        {
            return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.InvalidAccess);
        }

        var trackedShare = await _dbContext.EstimateShares
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == share.Id, cancellationToken);

        trackedShare.RecordAccess(_timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CustomerPortalOperationResult.Succeeded();
    }

    public async Task<CustomerEstimatePortalDetails?> GetSharedEstimateAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default)
    {
        var share = await FindActiveShareByPublicIdAsync(sharePublicId, cancellationToken);
        if (share is null)
        {
            return null;
        }

        _organizationContextMutator.Resolve(share.OrganizationId);

        var row = await (
            from estimate in _dbContext.Estimates.AsNoTracking()
            join repairOrder in _dbContext.RepairOrders.AsNoTracking()
                on estimate.RepairOrderId equals repairOrder.Id
            join vehicle in _dbContext.Vehicles.AsNoTracking()
                on repairOrder.VehicleId equals vehicle.Id
            join organization in _dbContext.Organizations.AsNoTracking()
                on estimate.OrganizationId equals organization.Id
            where estimate.Id == share.EstimateId
            select new
            {
                OrganizationName = organization.Name,
                estimate.Number,
                estimate.Status,
                estimate.CurrencyCode,
                estimate.CustomerMessage,
                estimate.SentAtUtc,
                estimate.ApprovedAtUtc,
                estimate.DeclinedAtUtc,
                RepairOrderNumber = repairOrder.Number,
                vehicle.Make,
                vehicle.Model,
                vehicle.ModelYear,
                vehicle.RegistrationPlate,
            }).SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var items = await _dbContext.EstimateItems.AsNoTracking()
            .Where(candidate => candidate.EstimateId == share.EstimateId)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new
            {
                candidate.Description,
                candidate.Quantity,
                candidate.UnitPrice,
            })
            .ToListAsync(cancellationToken);

        var portalItems = items
            .Select(item => new CustomerEstimatePortalItem
            {
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = EstimateMoneyCalculator.CalculateLineTotal(item.Quantity, item.UnitPrice),
            })
            .ToList();

        var total = EstimateMoneyCalculator.CalculateEstimateTotal(
            items.Select(item => (item.Quantity, item.UnitPrice)).ToList());

        var canDecide = row.Status == EstimateStatus.Sent && !share.Decision.HasValue;

        return new CustomerEstimatePortalDetails
        {
            OrganizationDisplayName = row.OrganizationName,
            EstimateNumber = row.Number,
            Status = row.Status,
            RepairOrderNumber = row.RepairOrderNumber,
            VehicleMake = row.Make,
            VehicleModel = row.Model,
            VehicleModelYear = row.ModelYear,
            VehicleRegistrationPlate = row.RegistrationPlate,
            CustomerMessage = row.CustomerMessage,
            CurrencyCode = row.CurrencyCode,
            Items = portalItems,
            Total = total,
            PresentedAtUtc = row.SentAtUtc,
            DecisionAtUtc = share.DecisionAtUtc ?? row.ApprovedAtUtc ?? row.DeclinedAtUtc,
            PortalDecision = share.Decision,
            CanApprove = canDecide,
            CanDecline = canDecide,
        };
    }

    public Task<CustomerPortalOperationResult> RecordApprovalAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default) =>
        RecordDecisionAsync(sharePublicId, EstimateShareDecision.Approved, cancellationToken);

    public Task<CustomerPortalOperationResult> RecordDeclineAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken = default) =>
        RecordDecisionAsync(sharePublicId, EstimateShareDecision.Declined, cancellationToken);

    private async Task<CustomerPortalOperationResult> RecordDecisionAsync(
        Guid sharePublicId,
        EstimateShareDecision decision,
        CancellationToken cancellationToken)
    {
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
            var share = await _dbContext.EstimateShares
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.PublicId == sharePublicId, cancellationToken);

            if (share is null || !IsShareAccessAllowed(share))
            {
                return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.InvalidAccess);
            }

            _organizationContextMutator.Resolve(share.OrganizationId);

            var estimate = await _dbContext.Estimates
                .SingleOrDefaultAsync(candidate => candidate.Id == share.EstimateId, cancellationToken);

            if (estimate is null)
            {
                return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.EstimateNotFound);
            }

            if (estimate.Status != EstimateStatus.Sent || share.Decision.HasValue)
            {
                return CustomerPortalOperationResult.Failed(
                    CustomerPortalOperationFailureReason.InvalidLifecycleTransition);
            }

            var decidedAtUtc = _timeProvider.GetUtcNow();
            if (decision == EstimateShareDecision.Approved)
            {
                estimate.RecordCustomerApproval(decidedAtUtc);
            }
            else
            {
                estimate.RecordCustomerDecline(decidedAtUtc);
            }

            share.RecordDecision(decision, decidedAtUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return CustomerPortalOperationResult.Succeeded();
        }
        catch (InvalidOperationException)
        {
            return CustomerPortalOperationResult.Failed(
                CustomerPortalOperationFailureReason.InvalidLifecycleTransition);
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return CustomerPortalOperationResult.Failed(CustomerPortalOperationFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<EstimateShare?> FindActiveShareByPublicIdAsync(
        Guid sharePublicId,
        CancellationToken cancellationToken)
    {
        var share = await _dbContext.EstimateShares
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PublicId == sharePublicId, cancellationToken);

        return share is not null && IsShareAccessAllowed(share) ? share : null;
    }

    private bool IsShareAccessAllowed(EstimateShare share)
    {
        if (share.IsRevoked)
        {
            return false;
        }

        return !share.IsExpired(_timeProvider.GetUtcNow());
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
