using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Estimates;

public sealed class EstimateManagementService : IEstimateManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly IEstimateNumberGenerator _numberGenerator;
    private readonly TimeProvider _timeProvider;

    public EstimateManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        IEstimateNumberGenerator numberGenerator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _numberGenerator = numberGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<EstimateListResult> ListEstimatesAsync(
        EstimateListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => EstimateListQuery.DefaultPageSize,
            > EstimateListQuery.MaxPageSize => EstimateListQuery.MaxPageSize,
            _ => query.PageSize,
        };

        var estimates = BuildEstimateProjection();
        estimates = ApplyFilters(estimates, query);

        var totalCount = await estimates.CountAsync(cancellationToken);

        var rows = await estimates
            .OrderByDescending(row => row.Estimate.CreatedAtUtc)
            .ThenByDescending(row => row.Estimate.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var estimateIds = rows.Select(row => row.Estimate.Id).ToList();
        var totalsByEstimate = await LoadTotalsByEstimateAsync(estimateIds, cancellationToken);

        var items = rows
            .Select(row => new EstimateListItem
            {
                EstimateId = row.Estimate.Id,
                Number = row.Estimate.Number,
                Status = row.Estimate.Status,
                CurrencyCode = row.Estimate.CurrencyCode,
                Total = totalsByEstimate.GetValueOrDefault(row.Estimate.Id, 0m),
                RepairOrderId = row.RepairOrder.Id,
                RepairOrderNumber = row.RepairOrder.Number,
                CustomerDisplayName = row.Customer.DisplayName,
                VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
                WorkshopLocationName = row.Location.Name,
                CreatedAtUtc = row.Estimate.CreatedAtUtc,
                SentAtUtc = row.Estimate.SentAtUtc,
            })
            .ToList();

        return new EstimateListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<EstimateDetails?> GetEstimateDetailsAsync(
        Guid estimateId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var row = await BuildEstimateProjection()
            .SingleOrDefaultAsync(candidate => candidate.Estimate.Id == estimateId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var items = await _dbContext.EstimateItems.AsNoTracking()
            .Where(candidate => candidate.EstimateId == estimateId)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var inspectionContext = await LoadInspectionFindingContextAsync(
            row.RepairOrder.Id,
            cancellationToken);

        return MapDetails(row, items, inspectionContext);
    }

    public async Task<RepairOrderEstimateSummary?> GetRepairOrderEstimateSummaryAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var repairOrderExists = await _dbContext.RepairOrders
            .AnyAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (!repairOrderExists)
        {
            return null;
        }

        var estimates = await _dbContext.Estimates.AsNoTracking()
            .Where(candidate => candidate.RepairOrderId == repairOrderId)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        if (estimates.Count == 0)
        {
            return new RepairOrderEstimateSummary
            {
                EstimateCount = 0,
            };
        }

        var latest = estimates[0];
        var latestItems = await _dbContext.EstimateItems.AsNoTracking()
            .Where(candidate => candidate.EstimateId == latest.Id)
            .Select(candidate => new { candidate.Quantity, candidate.UnitPrice })
            .ToListAsync(cancellationToken);

        var latestTotal = EstimateMoneyCalculator.CalculateEstimateTotal(
            latestItems.Select(item => (item.Quantity, item.UnitPrice)).ToList());

        return new RepairOrderEstimateSummary
        {
            EstimateCount = estimates.Count,
            LatestEstimateId = latest.Id,
            LatestEstimateStatus = latest.Status,
            LatestEstimateTotal = latestTotal,
            LatestEstimateCurrencyCode = latest.CurrencyCode,
            LatestEstimateCreatedAtUtc = latest.CreatedAtUtc,
        };
    }

    public async Task<EstimateOperationResult<Guid>> CreateEstimateAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.RepairOrderNotFound);
        }

        if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.RepairOrderNotEligible);
        }

        var organization = await _dbContext.Organizations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == organizationId, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var number = _numberGenerator.Generate(now);
        var estimate = new Estimate(
            organizationId,
            repairOrderId,
            number,
            organization.DefaultCurrencyCode);

        _dbContext.Estimates.Add(estimate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateOperationResult<Guid>.Succeeded(estimate.Id);
    }

    public async Task<EstimateOperationResult<Guid>> AddEstimateItemAsync(
        Guid actorUserId,
        AddEstimateItemCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        if (!EstimateInputValidator.TryNormalizeDescription(command.Description, out var description)
            || !EstimateInputValidator.IsValidQuantity(command.Quantity)
            || !EstimateInputValidator.IsValidUnitPrice(command.UnitPrice))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.InvalidInput);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EstimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.EstimateNotFound);
        }

        if (!EstimateLifecyclePolicy.IsFinanciallyMutable(estimate.Status))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
        }

        var itemCount = await _dbContext.EstimateItems
            .CountAsync(candidate => candidate.EstimateId == estimate.Id, cancellationToken);

        if (itemCount >= EstimateInputValidator.MaxItemsPerEstimate)
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.ItemLimitExceeded);
        }

        var maxSortOrder = await _dbContext.EstimateItems
            .Where(candidate => candidate.EstimateId == estimate.Id)
            .Select(candidate => (int?)candidate.SortOrder)
            .MaxAsync(cancellationToken);

        var sortOrder = (maxSortOrder ?? 0) + 10;

        var item = new EstimateItem(
            organizationId,
            estimate.Id,
            command.Type,
            description,
            command.Quantity,
            command.UnitPrice,
            sortOrder);

        _dbContext.EstimateItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateOperationResult<Guid>.Succeeded(item.Id);
    }

    public async Task<EstimateOperationResult> UpdateEstimateItemAsync(
        Guid actorUserId,
        UpdateEstimateItemCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        if (!EstimateInputValidator.TryNormalizeDescription(command.Description, out var description)
            || !EstimateInputValidator.IsValidQuantity(command.Quantity)
            || !EstimateInputValidator.IsValidUnitPrice(command.UnitPrice))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidInput);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EstimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.EstimateNotFound);
        }

        if (!EstimateLifecyclePolicy.IsFinanciallyMutable(estimate.Status))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
        }

        var item = await _dbContext.EstimateItems
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.EstimateItemId && candidate.EstimateId == command.EstimateId,
                cancellationToken);

        if (item is null)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.ItemNotFound);
        }

        item.Update(command.Type, description, command.Quantity, command.UnitPrice, item.SortOrder);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateOperationResult.Succeeded();
    }

    public async Task<EstimateOperationResult> RemoveEstimateItemAsync(
        Guid actorUserId,
        Guid estimateId,
        Guid estimateItemId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.EstimateNotFound);
        }

        if (!EstimateLifecyclePolicy.IsFinanciallyMutable(estimate.Status))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
        }

        var item = await _dbContext.EstimateItems
            .SingleOrDefaultAsync(
                candidate => candidate.Id == estimateItemId && candidate.EstimateId == estimateId,
                cancellationToken);

        if (item is null)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.ItemNotFound);
        }

        _dbContext.EstimateItems.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateOperationResult.Succeeded();
    }

    public async Task<EstimateOperationResult> UpdateEstimateCustomerMessageAsync(
        Guid actorUserId,
        Guid estimateId,
        string? customerMessage,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        if (!EstimateInputValidator.TryNormalizeCustomerMessage(customerMessage, out var normalized))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidInput);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.EstimateNotFound);
        }

        if (!EstimateLifecyclePolicy.IsFinanciallyMutable(estimate.Status))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
        }

        estimate.UpdateCustomerMessage(normalized);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateOperationResult.Succeeded();
    }

    public Task<EstimateOperationResult> PresentForApprovalAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default) =>
        ExecuteLifecycleMutationAsync(
            actorUserId,
            estimateId,
            cancellationToken,
            async estimate =>
            {
                if (!EstimateLifecyclePolicy.CanPresent(estimate.Status))
                {
                    return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
                }

                var itemCount = await _dbContext.EstimateItems
                    .CountAsync(candidate => candidate.EstimateId == estimate.Id, cancellationToken);

                if (itemCount == 0)
                {
                    return EstimateOperationResult.Failed(EstimateOperationFailureReason.EmptyEstimate);
                }

                estimate.PresentForApproval(_timeProvider.GetUtcNow());
                return EstimateOperationResult.Succeeded();
            });

    public Task<EstimateOperationResult> RecordCustomerApprovalAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default) =>
        ExecuteLifecycleMutationAsync(
            actorUserId,
            estimateId,
            cancellationToken,
            estimate =>
            {
                if (!EstimateLifecyclePolicy.CanRecordCustomerDecision(estimate.Status))
                {
                    return Task.FromResult(
                        EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition));
                }

                estimate.RecordCustomerApproval(_timeProvider.GetUtcNow());
                return Task.FromResult(EstimateOperationResult.Succeeded());
            });

    public Task<EstimateOperationResult> RecordCustomerDeclineAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken = default) =>
        ExecuteLifecycleMutationAsync(
            actorUserId,
            estimateId,
            cancellationToken,
            estimate =>
            {
                if (!EstimateLifecyclePolicy.CanRecordCustomerDecision(estimate.Status))
                {
                    return Task.FromResult(
                        EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition));
                }

                estimate.RecordCustomerDecline(_timeProvider.GetUtcNow());
                return Task.FromResult(EstimateOperationResult.Succeeded());
            });

    public Task<EstimateOperationResult<Guid>> AddServiceCatalogItemAsync(
        Guid actorUserId,
        AddServiceCatalogItemToEstimateCommand command,
        CancellationToken cancellationToken = default) =>
        AddCatalogItemAsync(
            actorUserId,
            command.EstimateId,
            command.Quantity,
            cancellationToken,
            async estimate =>
            {
                var service = await _dbContext.ServiceCatalogItems.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == command.ServiceCatalogItemId, cancellationToken);

                if (service is null)
                {
                    return (EstimateOperationFailureReason.CatalogItemNotFound, null, null, null);
                }

                if (!service.IsActive)
                {
                    return (EstimateOperationFailureReason.CatalogItemInactive, null, null, null);
                }

                if (!string.Equals(service.CurrencyCode, estimate.CurrencyCode, StringComparison.Ordinal))
                {
                    return (EstimateOperationFailureReason.CurrencyMismatch, null, null, null);
                }

                return (
                    null,
                    EstimateItemType.Service,
                    service.BuildEstimateDescription(),
                    service.DefaultUnitPrice);
            });

    public Task<EstimateOperationResult<Guid>> AddPartCatalogItemAsync(
        Guid actorUserId,
        AddPartCatalogItemToEstimateCommand command,
        CancellationToken cancellationToken = default) =>
        AddCatalogItemAsync(
            actorUserId,
            command.EstimateId,
            command.Quantity,
            cancellationToken,
            async estimate =>
            {
                var part = await _dbContext.PartCatalogItems.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == command.PartCatalogItemId, cancellationToken);

                if (part is null)
                {
                    return (EstimateOperationFailureReason.CatalogItemNotFound, null, null, null);
                }

                if (!part.IsActive)
                {
                    return (EstimateOperationFailureReason.CatalogItemInactive, null, null, null);
                }

                if (!string.Equals(part.CurrencyCode, estimate.CurrencyCode, StringComparison.Ordinal))
                {
                    return (EstimateOperationFailureReason.CurrencyMismatch, null, null, null);
                }

                return (
                    null,
                    EstimateItemType.Part,
                    part.BuildEstimateDescription(),
                    part.DefaultUnitPrice);
            });

    private async Task<EstimateOperationResult<Guid>> AddCatalogItemAsync(
        Guid actorUserId,
        Guid estimateId,
        decimal quantity,
        CancellationToken cancellationToken,
        Func<Estimate, Task<(EstimateOperationFailureReason? Failure, EstimateItemType? Type, string? Description, decimal? UnitPrice)>> resolveSnapshot)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        if (!EstimateInputValidator.IsValidQuantity(quantity))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.InvalidInput);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.EstimateNotFound);
        }

        if (!EstimateLifecyclePolicy.IsFinanciallyMutable(estimate.Status))
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
        }

        var snapshot = await resolveSnapshot(estimate);
        if (snapshot.Failure.HasValue)
        {
            return EstimateOperationResult<Guid>.Failed(snapshot.Failure.Value);
        }

        var itemCount = await _dbContext.EstimateItems
            .CountAsync(candidate => candidate.EstimateId == estimate.Id, cancellationToken);

        if (itemCount >= EstimateInputValidator.MaxItemsPerEstimate)
        {
            return EstimateOperationResult<Guid>.Failed(EstimateOperationFailureReason.ItemLimitExceeded);
        }

        var maxSortOrder = await _dbContext.EstimateItems
            .Where(candidate => candidate.EstimateId == estimate.Id)
            .Select(candidate => (int?)candidate.SortOrder)
            .MaxAsync(cancellationToken);

        var sortOrder = (maxSortOrder ?? 0) + 10;

        var item = new EstimateItem(
            organizationId,
            estimate.Id,
            snapshot.Type!.Value,
            snapshot.Description!,
            quantity,
            snapshot.UnitPrice!.Value,
            sortOrder);

        _dbContext.EstimateItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateOperationResult<Guid>.Succeeded(item.Id);
    }

    private async Task<EstimateOperationResult> ExecuteLifecycleMutationAsync(
        Guid actorUserId,
        Guid estimateId,
        CancellationToken cancellationToken,
        Func<Estimate, Task<EstimateOperationResult>> mutate)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.Unauthorized);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.EstimateNotFound);
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
            var result = await mutate(estimate);
            if (!result.Success)
            {
                return result;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return EstimateOperationResult.Succeeded();
        }
        catch (InvalidOperationException)
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.InvalidLifecycleTransition);
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return EstimateOperationResult.Failed(EstimateOperationFailureReason.ConcurrencyConflict);
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
        return EstimateManagerPolicy.CanManageEstimates(role);
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

    private IQueryable<EstimateProjectionRow> BuildEstimateProjection()
    {
        return from estimate in _dbContext.Estimates.AsNoTracking()
               join repairOrder in _dbContext.RepairOrders.AsNoTracking()
                   on estimate.RepairOrderId equals repairOrder.Id
               join location in _dbContext.WorkshopLocations.AsNoTracking()
                   on repairOrder.WorkshopLocationId equals location.Id
               join customer in _dbContext.Customers.AsNoTracking()
                   on repairOrder.CustomerId equals customer.Id
               join vehicle in _dbContext.Vehicles.AsNoTracking()
                   on repairOrder.VehicleId equals vehicle.Id
               select new EstimateProjectionRow
               {
                   Estimate = estimate,
                   RepairOrder = repairOrder,
                   Location = location,
                   Customer = customer,
                   Vehicle = vehicle,
               };
    }

    private static IQueryable<EstimateProjectionRow> ApplyFilters(
        IQueryable<EstimateProjectionRow> estimates,
        EstimateListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            estimates = estimates.Where(row =>
                row.Estimate.Number.Contains(term)
                || row.RepairOrder.Number.Contains(term)
                || row.Customer.DisplayName.Contains(term)
                || row.Vehicle.Make.Contains(term)
                || row.Vehicle.Model.Contains(term)
                || (row.Vehicle.RegistrationPlate != null && row.Vehicle.RegistrationPlate.Contains(term)));
        }

        if (query.Status.HasValue)
        {
            estimates = estimates.Where(row => row.Estimate.Status == query.Status.Value);
        }

        if (query.RepairOrderId.HasValue)
        {
            estimates = estimates.Where(row => row.Estimate.RepairOrderId == query.RepairOrderId.Value);
        }

        if (query.WorkshopLocationId.HasValue)
        {
            estimates = estimates.Where(row =>
                row.RepairOrder.WorkshopLocationId == query.WorkshopLocationId.Value);
        }

        if (query.CreatedFromUtc.HasValue)
        {
            estimates = estimates.Where(row => row.Estimate.CreatedAtUtc >= query.CreatedFromUtc.Value);
        }

        if (query.CreatedToUtc.HasValue)
        {
            estimates = estimates.Where(row => row.Estimate.CreatedAtUtc <= query.CreatedToUtc.Value);
        }

        return estimates;
    }

    private async Task<Dictionary<Guid, decimal>> LoadTotalsByEstimateAsync(
        IReadOnlyList<Guid> estimateIds,
        CancellationToken cancellationToken)
    {
        if (estimateIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var items = await _dbContext.EstimateItems.AsNoTracking()
            .Where(candidate => estimateIds.Contains(candidate.EstimateId))
            .Select(candidate => new
            {
                candidate.EstimateId,
                candidate.Quantity,
                candidate.UnitPrice,
            })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(item => item.EstimateId)
            .ToDictionary(
                group => group.Key,
                group => EstimateMoneyCalculator.CalculateEstimateTotal(
                    group.Select(item => (item.Quantity, item.UnitPrice)).ToList()));
    }

    private async Task<(Guid? LatestInspectionId, IReadOnlyList<InspectionFindingContext> Findings)> LoadInspectionFindingContextAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken)
    {
        var latestInspection = await _dbContext.Inspections.AsNoTracking()
            .Where(candidate => candidate.RepairOrderId == repairOrderId)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestInspection is null)
        {
            return (null, Array.Empty<InspectionFindingContext>());
        }

        var findings = await _dbContext.InspectionItems.AsNoTracking()
            .Where(candidate => candidate.InspectionId == latestInspection.Id
                                && candidate.Condition != InspectionCondition.NotChecked
                                && candidate.Condition != InspectionCondition.Good)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new InspectionFindingContext
            {
                Section = candidate.Section,
                Name = candidate.Name,
                Condition = candidate.Condition,
                Notes = candidate.Notes,
            })
            .ToListAsync(cancellationToken);

        return (latestInspection.Id, findings);
    }

    private static EstimateDetails MapDetails(
        EstimateProjectionRow row,
        IReadOnlyList<EstimateItem> items,
        (Guid? LatestInspectionId, IReadOnlyList<InspectionFindingContext> Findings) inspectionContext)
    {
        var itemDetails = items
            .Select(item => new EstimateItemDetails
            {
                EstimateItemId = item.Id,
                Type = item.Type,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = EstimateMoneyCalculator.CalculateLineTotal(item.Quantity, item.UnitPrice),
                SortOrder = item.SortOrder,
            })
            .ToList();

        var total = EstimateMoneyCalculator.CalculateEstimateTotal(
            items.Select(item => (item.Quantity, item.UnitPrice)).ToList());

        return new EstimateDetails
        {
            EstimateId = row.Estimate.Id,
            Number = row.Estimate.Number,
            Status = row.Estimate.Status,
            RepairOrderId = row.RepairOrder.Id,
            RepairOrderNumber = row.RepairOrder.Number,
            CustomerDisplayName = row.Customer.DisplayName,
            VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            WorkshopLocationName = row.Location.Name,
            WorkshopLocationId = row.Location.Id,
            CurrencyCode = row.Estimate.CurrencyCode,
            Total = total,
            CustomerMessage = row.Estimate.CustomerMessage,
            Items = itemDetails,
            InspectionFindings = inspectionContext.Findings,
            LatestInspectionId = inspectionContext.LatestInspectionId,
            CreatedAtUtc = row.Estimate.CreatedAtUtc,
            UpdatedAtUtc = row.Estimate.UpdatedAtUtc,
            SentAtUtc = row.Estimate.SentAtUtc,
            ApprovedAtUtc = row.Estimate.ApprovedAtUtc,
            DeclinedAtUtc = row.Estimate.DeclinedAtUtc,
            CanEditItems = EstimateLifecyclePolicy.IsFinanciallyMutable(row.Estimate.Status),
            CanPresent = EstimateLifecyclePolicy.CanPresent(row.Estimate.Status) && items.Count > 0,
            CanRecordApproval = EstimateLifecyclePolicy.CanRecordCustomerDecision(row.Estimate.Status),
            CanRecordDecline = EstimateLifecyclePolicy.CanRecordCustomerDecision(row.Estimate.Status),
        };
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

    private static EstimateListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<EstimateListItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = EstimateListQuery.DefaultPageSize,
        };

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private sealed class EstimateProjectionRow
    {
        public required Estimate Estimate { get; init; }

        public required RepairOrder RepairOrder { get; init; }

        public required WorkshopLocation Location { get; init; }

        public required Domain.Customers.Customer Customer { get; init; }

        public required Domain.Vehicles.Vehicle Vehicle { get; init; }
    }
}
