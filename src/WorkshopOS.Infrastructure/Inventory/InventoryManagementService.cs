using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Inventory;

public sealed class InventoryManagementService : IInventoryManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public InventoryManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<InventoryListResult> ListInventoryAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = NormalizePageSize(query.PageSize);

        var balances = from balance in _dbContext.PartInventoryBalances.AsNoTracking()
                       join part in _dbContext.PartCatalogItems.AsNoTracking()
                           on balance.PartCatalogItemId equals part.Id
                       join location in _dbContext.WorkshopLocations.AsNoTracking()
                           on balance.WorkshopLocationId equals location.Id
                       select new { balance, part, location };

        if (query.WorkshopLocationId.HasValue)
        {
            balances = balances.Where(row => row.balance.WorkshopLocationId == query.WorkshopLocationId.Value);
        }

        if (query.PartCatalogItemId.HasValue)
        {
            balances = balances.Where(row => row.balance.PartCatalogItemId == query.PartCatalogItemId.Value);
        }

        if (query.LowOrZeroStockOnly == true)
        {
            balances = balances.Where(row => row.balance.QuantityOnHand <= 0);
        }

        var totalCount = await balances.CountAsync(cancellationToken);

        var items = await balances
            .OrderBy(row => row.part.Name)
            .ThenBy(row => row.part.Sku)
            .ThenBy(row => row.location.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new InventoryBalanceItem
            {
                PartCatalogItemId = row.part.Id,
                PartSku = row.part.Sku,
                PartName = row.part.Name,
                PartIsActive = row.part.IsActive,
                WorkshopLocationId = row.location.Id,
                WorkshopLocationName = row.location.Name,
                QuantityOnHand = row.balance.QuantityOnHand,
            })
            .ToListAsync(cancellationToken);

        return new InventoryListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<InventoryMovementHistoryResult> GetMovementHistoryAsync(
        InventoryMovementHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyHistoryResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = NormalizeHistoryPageSize(query.PageSize);

        var movements = _dbContext.PartInventoryMovements.AsNoTracking()
            .Where(movement => movement.PartCatalogItemId == query.PartCatalogItemId
                               && movement.WorkshopLocationId == query.WorkshopLocationId);

        var totalCount = await movements.CountAsync(cancellationToken);

        var items = await movements
            .OrderByDescending(movement => movement.OccurredAtUtc)
            .ThenByDescending(movement => movement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(movement => new InventoryMovementItem
            {
                MovementId = movement.Id,
                MovementType = movement.MovementType,
                QuantityDelta = movement.QuantityDelta,
                BalanceAfter = movement.BalanceAfter,
                Reason = movement.Reason,
                RecordedByUserId = movement.RecordedByUserId,
                OccurredAtUtc = movement.OccurredAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new InventoryMovementHistoryResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<decimal?> GetQuantityOnHandAsync(
        Guid partCatalogItemId,
        Guid workshopLocationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var balance = await _dbContext.PartInventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PartCatalogItemId == partCatalogItemId
                             && candidate.WorkshopLocationId == workshopLocationId,
                cancellationToken);

        return balance?.QuantityOnHand ?? 0m;
    }

    public async Task<InventoryAdjustmentResult> AdjustInventoryAsync(
        Guid actorUserId,
        AdjustInventoryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateInventoryManagerAsync(actorUserId, cancellationToken))
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.Unauthorized);
        }

        if (!InventoryInputValidator.IsValidQuantity(command.Quantity)
            || !InventoryInputValidator.TryNormalizeReason(command.Reason, out var reason))
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.InvalidInput);
        }

        var quantityDelta = ComputeSignedDelta(command.MovementType, command.Quantity);
        if (quantityDelta == 0)
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.InvalidInput);
        }

        var part = await _dbContext.PartCatalogItems.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.PartCatalogItemId, cancellationToken);

        if (part is null)
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.PartNotFound);
        }

        if (!part.IsActive)
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.PartInactive);
        }

        var location = await _dbContext.WorkshopLocations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.WorkshopLocationId, cancellationToken);

        if (location is null)
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.LocationNotFound);
        }

        if (!location.IsActive)
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.LocationInactive);
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
            var balance = await _dbContext.PartInventoryBalances
                .SingleOrDefaultAsync(
                    candidate => candidate.PartCatalogItemId == command.PartCatalogItemId
                                 && candidate.WorkshopLocationId == command.WorkshopLocationId,
                    cancellationToken);

            if (balance is null)
            {
                balance = new PartInventoryBalance(
                    organizationId,
                    command.PartCatalogItemId,
                    command.WorkshopLocationId,
                    0m);
                _dbContext.PartInventoryBalances.Add(balance);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (IsUniqueViolation(exception))
                {
                    _dbContext.Entry(balance).State = EntityState.Detached;
                    balance = await _dbContext.PartInventoryBalances
                        .SingleAsync(
                            candidate => candidate.PartCatalogItemId == command.PartCatalogItemId
                                         && candidate.WorkshopLocationId == command.WorkshopLocationId,
                            cancellationToken);
                }
            }

            var newQuantity = balance.QuantityOnHand + quantityDelta;
            if (newQuantity < 0)
            {
                return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.InsufficientStock);
            }

            balance.ApplyDelta(quantityDelta);

            var occurredAtUtc = _timeProvider.GetUtcNow();
            var movement = new PartInventoryMovement(
                organizationId,
                command.PartCatalogItemId,
                command.WorkshopLocationId,
                command.MovementType,
                quantityDelta,
                newQuantity,
                actorUserId,
                occurredAtUtc,
                reason);

            _dbContext.PartInventoryMovements.Add(movement);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return InventoryAdjustmentResult.Succeeded(movement.Id, newQuantity);
        }
        catch (InvalidOperationException)
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.InsufficientStock);
        }
        catch (DbUpdateException exception) when (IsSerializationFailure(exception) || IsUniqueViolation(exception))
        {
            return InventoryAdjustmentResult.Failed(InventoryAdjustmentFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static decimal ComputeSignedDelta(PartInventoryMovementType movementType, decimal quantity) =>
        movementType switch
        {
            PartInventoryMovementType.OpeningBalance => quantity,
            PartInventoryMovementType.ManualIncrease => quantity,
            PartInventoryMovementType.ManualDecrease => -quantity,
            _ => 0,
        };

    private async Task<bool> ValidateInventoryManagerAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var role = await ResolveMembershipRoleAsync(actorUserId, cancellationToken);
        return InventoryManagerPolicy.CanManageInventory(role);
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

        organizationId = default;
        return false;
    }

    private static InventoryListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<InventoryBalanceItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = InventoryListQuery.DefaultPageSize,
        };

    private static InventoryMovementHistoryResult EmptyHistoryResult() =>
        new()
        {
            Items = Array.Empty<InventoryMovementItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = InventoryMovementHistoryQuery.DefaultPageSize,
        };

    private static int NormalizePageSize(int pageSize) =>
        pageSize switch
        {
            < 1 => InventoryListQuery.DefaultPageSize,
            > InventoryListQuery.MaxPageSize => InventoryListQuery.MaxPageSize,
            _ => pageSize,
        };

    private static int NormalizeHistoryPageSize(int pageSize) =>
        pageSize switch
        {
            < 1 => InventoryMovementHistoryQuery.DefaultPageSize,
            > InventoryMovementHistoryQuery.MaxPageSize => InventoryMovementHistoryQuery.MaxPageSize,
            _ => pageSize,
        };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsSerializationFailure(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.SerializationFailure;
}
