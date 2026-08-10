using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Inspections;

public sealed class InspectionManagementService : IInspectionManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public InspectionManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<InspectionListResult> ListInspectionsAsync(
        InspectionListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => InspectionListQuery.DefaultPageSize,
            > InspectionListQuery.MaxPageSize => InspectionListQuery.MaxPageSize,
            _ => query.PageSize,
        };

        var inspections = BuildInspectionProjection();
        inspections = ApplyFilters(inspections, query);

        var totalCount = await inspections.CountAsync(cancellationToken);

        var rows = await inspections
            .OrderByDescending(row => row.Inspection.CreatedAtUtc)
            .ThenBy(row => row.Inspection.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var inspectionIds = rows.Select(row => row.Inspection.Id).ToList();
        var itemCounts = await _dbContext.InspectionItems.AsNoTracking()
            .Where(candidate => inspectionIds.Contains(candidate.InspectionId))
            .GroupBy(candidate => candidate.InspectionId)
            .Select(group => new
            {
                InspectionId = group.Key,
                Total = group.Count(),
                Inspected = group.Count(item => item.Condition != InspectionCondition.NotChecked),
            })
            .ToDictionaryAsync(candidate => candidate.InspectionId, cancellationToken);

        var items = rows
            .Select(row =>
            {
                var counts = itemCounts.GetValueOrDefault(row.Inspection.Id);
                return new InspectionListItem
                {
                    InspectionId = row.Inspection.Id,
                    RepairOrderId = row.Inspection.RepairOrderId,
                    RepairOrderNumber = row.RepairOrder.Number,
                    Status = row.Inspection.Status,
                    CustomerDisplayName = row.Customer.DisplayName,
                    VehicleSummary = FormatVehicleSummary(
                        row.Vehicle.Make,
                        row.Vehicle.Model,
                        row.Vehicle.ModelYear),
                    WorkshopLocationName = row.Location.Name,
                    TotalItems = counts?.Total ?? 0,
                    InspectedItems = counts?.Inspected ?? 0,
                    CreatedAtUtc = row.Inspection.CreatedAtUtc,
                    CompletedAtUtc = row.Inspection.CompletedAtUtc,
                };
            })
            .ToList();

        return new InspectionListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<InspectionDetails?> GetInspectionDetailsAsync(
        Guid inspectionId,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var row = await BuildInspectionProjection()
            .SingleOrDefaultAsync(candidate => candidate.Inspection.Id == inspectionId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var items = await _dbContext.InspectionItems.AsNoTracking()
            .Where(candidate => candidate.InspectionId == inspectionId)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var mediaAssets = await _dbContext.InspectionMediaAssets.AsNoTracking()
            .Where(candidate => candidate.InspectionId == inspectionId && candidate.RemovedAtUtc == null)
            .OrderBy(candidate => candidate.UploadedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        if (actorUserId is null)
        {
            return MapDetails(
                row,
                items,
                mediaAssets,
                canStart: false,
                canUpdateItems: false,
                canComplete: false,
                canMutateMedia: false);
        }

        var canExecute = await ValidateExecutionAsync(actorUserId.Value, row.Inspection.RepairOrderId, cancellationToken);
        var canMutateMedia = canExecute.Success && row.Inspection.Status == InspectionStatus.InProgress;

        return MapDetails(
            row,
            items,
            mediaAssets,
            canStart: canExecute.Success && InspectionLifecyclePolicy.CanStart(row.Inspection.Status),
            canUpdateItems: canExecute.Success && InspectionLifecyclePolicy.CanUpdateItems(row.Inspection.Status),
            canComplete: canExecute.Success && InspectionLifecyclePolicy.CanComplete(row.Inspection.Status),
            canMutateMedia: canMutateMedia);
    }

    public async Task<RepairOrderInspectionSummary?> GetRepairOrderInspectionSummaryAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var repairOrderExists = await _dbContext.RepairOrders.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (!repairOrderExists)
        {
            return null;
        }

        var inspections = await _dbContext.Inspections.AsNoTracking()
            .Where(candidate => candidate.RepairOrderId == repairOrderId)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var latest = inspections.FirstOrDefault();

        return new RepairOrderInspectionSummary
        {
            InspectionCount = inspections.Count,
            LatestInspectionId = latest?.Id,
            LatestInspectionStatus = latest?.Status,
            LatestInspectionCreatedAtUtc = latest?.CreatedAtUtc,
            LatestInspectionCompletedAtUtc = latest?.CompletedAtUtc,
        };
    }

    public async Task<InspectionOperationResult<Guid>> CreateInspectionAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return InspectionOperationResult<Guid>.Failed(
                InspectionOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return InspectionOperationResult<Guid>.Failed(InspectionOperationFailureReason.Unauthorized);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return InspectionOperationResult<Guid>.Failed(InspectionOperationFailureReason.RepairOrderNotFound);
        }

        if (!IsRepairOrderEligibleForInspection(repairOrder.Status))
        {
            return InspectionOperationResult<Guid>.Failed(InspectionOperationFailureReason.RepairOrderNotEligible);
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var inspection = new Inspection(organizationId, repairOrderId);
            _dbContext.Inspections.Add(inspection);

            foreach (var templateItem in DefaultVehicleInspectionTemplate.Items)
            {
                _dbContext.InspectionItems.Add(new InspectionItem(
                    organizationId,
                    inspection.Id,
                    templateItem.Section,
                    templateItem.Name,
                    InspectionCondition.NotChecked,
                    templateItem.SortOrder));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            return InspectionOperationResult<Guid>.Succeeded(inspection.Id);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public Task<InspectionOperationResult> StartInspectionAsync(
        Guid actorUserId,
        Guid inspectionId,
        CancellationToken cancellationToken = default) =>
        ExecuteInspectionMutationAsync(
            actorUserId,
            inspectionId,
            cancellationToken,
            inspection =>
            {
                if (!InspectionLifecyclePolicy.CanStart(inspection.Status))
                {
                    return Task.FromResult(InspectionOperationResult.Failed(
                        InspectionOperationFailureReason.InvalidLifecycleTransition));
                }

                inspection.Start(_timeProvider.GetUtcNow());
                return Task.FromResult(InspectionOperationResult.Succeeded());
            });

    public Task<InspectionOperationResult> UpdateInspectionItemsAsync(
        Guid actorUserId,
        UpdateInspectionItemsCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteInspectionMutationAsync(
            actorUserId,
            command.InspectionId,
            cancellationToken,
            async inspection =>
            {
                if (!InspectionLifecyclePolicy.CanUpdateItems(inspection.Status))
                {
                    return InspectionOperationResult.Failed(
                        InspectionOperationFailureReason.InvalidLifecycleTransition);
                }

                if (command.Items.Count == 0)
                {
                    return InspectionOperationResult.Failed(InspectionOperationFailureReason.InvalidInput);
                }

                var itemIds = command.Items.Select(item => item.InspectionItemId).ToList();
                if (itemIds.Distinct().Count() != itemIds.Count)
                {
                    return InspectionOperationResult.Failed(
                        InspectionOperationFailureReason.DuplicateItemSubmission);
                }

                var items = await _dbContext.InspectionItems
                    .Where(candidate => candidate.InspectionId == inspection.Id)
                    .ToListAsync(cancellationToken);

                var itemMap = items.ToDictionary(candidate => candidate.Id);

                foreach (var update in command.Items)
                {
                    if (!itemMap.TryGetValue(update.InspectionItemId, out var item))
                    {
                        return InspectionOperationResult.Failed(InspectionOperationFailureReason.ItemNotFound);
                    }

                    if (update.Notes is { Length: > 2000 })
                    {
                        return InspectionOperationResult.Failed(InspectionOperationFailureReason.InvalidInput);
                    }

                    item.UpdateResult(update.Condition, update.Notes);
                }

                return InspectionOperationResult.Succeeded();
            },
            useSerializable: true);

    public Task<InspectionOperationResult> CompleteInspectionAsync(
        Guid actorUserId,
        Guid inspectionId,
        CancellationToken cancellationToken = default) =>
        ExecuteInspectionMutationAsync(
            actorUserId,
            inspectionId,
            cancellationToken,
            async inspection =>
            {
                if (!InspectionLifecyclePolicy.CanComplete(inspection.Status))
                {
                    return InspectionOperationResult.Failed(
                        InspectionOperationFailureReason.InvalidLifecycleTransition);
                }

                var items = await _dbContext.InspectionItems
                    .Where(candidate => candidate.InspectionId == inspection.Id)
                    .ToListAsync(cancellationToken);

                if (items.Count == 0)
                {
                    return InspectionOperationResult.Failed(InspectionOperationFailureReason.EmptyInspection);
                }

                if (items.Any(item => !InspectionLifecyclePolicy.IsInspected(item.Condition)))
                {
                    return InspectionOperationResult.Failed(
                        InspectionOperationFailureReason.ItemsNotFullyInspected);
                }

                inspection.Complete(_timeProvider.GetUtcNow());
                return InspectionOperationResult.Succeeded();
            },
            useSerializable: true);

    private async Task<InspectionOperationResult> ExecuteInspectionMutationAsync(
        Guid actorUserId,
        Guid inspectionId,
        CancellationToken cancellationToken,
        Func<Inspection, Task<InspectionOperationResult>> mutate,
        bool useSerializable = false)
    {
        if (!TryGetOrganizationId(out _))
        {
            return InspectionOperationResult.Failed(InspectionOperationFailureReason.OrganizationUnresolved);
        }

        var inspection = await _dbContext.Inspections
            .SingleOrDefaultAsync(candidate => candidate.Id == inspectionId, cancellationToken);

        if (inspection is null)
        {
            return InspectionOperationResult.Failed(InspectionOperationFailureReason.InspectionNotFound);
        }

        var execution = await ValidateExecutionAsync(actorUserId, inspection.RepairOrderId, cancellationToken);
        if (!execution.Success)
        {
            return InspectionOperationResult.Failed(execution.FailureReason!.Value);
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction && useSerializable)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        }

        try
        {
            var result = await mutate(inspection);
            if (!result.Success)
            {
                return result;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return InspectionOperationResult.Succeeded();
        }
        catch (InvalidOperationException)
        {
            return InspectionOperationResult.Failed(InspectionOperationFailureReason.InvalidLifecycleTransition);
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return InspectionOperationResult.Failed(InspectionOperationFailureReason.ConcurrencyConflict);
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
        return InspectionManagerPolicy.CanManageInspections(role);
    }

    private async Task<(bool Success, InspectionOperationFailureReason? FailureReason)> ValidateExecutionAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken)
    {
        if (await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return (true, null);
        }

        if (!TryGetOrganizationId(out var organizationId))
        {
            return (false, InspectionOperationFailureReason.OrganizationUnresolved);
        }

        var membership = await _dbContext.OrganizationMemberships.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == actorUserId && candidate.OrganizationId == organizationId,
                cancellationToken);

        if (membership is null || membership.Status != OrganizationMembershipStatus.Active)
        {
            return (false, InspectionOperationFailureReason.MembershipInactive);
        }

        var linkedStaff = await ResolveLinkedStaffMemberAsync(actorUserId, cancellationToken);
        if (linkedStaff is null)
        {
            return (false, InspectionOperationFailureReason.TechnicianProfileNotLinked);
        }

        if (linkedStaff.Status != StaffStatus.Active)
        {
            return (false, InspectionOperationFailureReason.StaffInactive);
        }

        if (linkedStaff.Position != StaffPosition.Technician)
        {
            return (false, InspectionOperationFailureReason.Unauthorized);
        }

        var isAssigned = await _dbContext.RepairOrderTechnicianAssignments.AnyAsync(
            assignment => assignment.RepairOrderId == repairOrderId
                          && assignment.StaffMemberId == linkedStaff.Id
                          && assignment.UnassignedAtUtc == null,
            cancellationToken);

        return isAssigned
            ? (true, null)
            : (false, InspectionOperationFailureReason.Unauthorized);
    }

    private static bool IsRepairOrderEligibleForInspection(RepairOrderStatus status) =>
        !RepairOrderLifecyclePolicy.IsTerminal(status);

    private IQueryable<InspectionProjectionRow> BuildInspectionProjection() =>
        from inspection in _dbContext.Inspections.AsNoTracking()
        join repairOrder in _dbContext.RepairOrders.AsNoTracking()
            on inspection.RepairOrderId equals repairOrder.Id
        join location in _dbContext.WorkshopLocations.AsNoTracking()
            on repairOrder.WorkshopLocationId equals location.Id
        join customer in _dbContext.Customers.AsNoTracking()
            on repairOrder.CustomerId equals customer.Id
        join vehicle in _dbContext.Vehicles.AsNoTracking()
            on repairOrder.VehicleId equals vehicle.Id
        select new InspectionProjectionRow
        {
            Inspection = inspection,
            RepairOrder = repairOrder,
            Location = location,
            Customer = customer,
            Vehicle = vehicle,
        };

    private static IQueryable<InspectionProjectionRow> ApplyFilters(
        IQueryable<InspectionProjectionRow> inspections,
        InspectionListQuery query)
    {
        if (query.Status.HasValue)
        {
            inspections = inspections.Where(row => row.Inspection.Status == query.Status.Value);
        }

        if (query.RepairOrderId.HasValue)
        {
            inspections = inspections.Where(row => row.Inspection.RepairOrderId == query.RepairOrderId.Value);
        }

        if (query.WorkshopLocationId.HasValue)
        {
            inspections = inspections.Where(row =>
                row.RepairOrder.WorkshopLocationId == query.WorkshopLocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            inspections = inspections.Where(row =>
                EF.Functions.ILike(row.RepairOrder.Number, pattern)
                || EF.Functions.ILike(row.Customer.DisplayName, pattern)
                || EF.Functions.ILike(row.Vehicle.Make, pattern)
                || EF.Functions.ILike(row.Vehicle.Model, pattern));
        }

        return inspections;
    }

    private static InspectionDetails MapDetails(
        InspectionProjectionRow row,
        IReadOnlyList<InspectionItem> items,
        IReadOnlyList<InspectionMediaAsset> mediaAssets,
        bool canStart,
        bool canUpdateItems,
        bool canComplete,
        bool canMutateMedia)
    {
        var mediaByItem = mediaAssets
            .GroupBy(asset => asset.InspectionItemId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var summary = BuildSummary(items);
        var sections = items
            .GroupBy(item => item.Section)
            .OrderBy(group => group.Min(item => item.SortOrder))
            .Select(group => new InspectionSectionDetails
            {
                Section = group.Key,
                Items = group
                    .OrderBy(item => item.SortOrder)
                    .Select(item => new InspectionItemDetails
                    {
                        InspectionItemId = item.Id,
                        Name = item.Name,
                        Condition = item.Condition,
                        Notes = item.Notes,
                        SortOrder = item.SortOrder,
                        Media = mediaByItem.TryGetValue(item.Id, out var itemMedia)
                            ? itemMedia
                                .Select(asset => new InspectionMediaItem
                                {
                                    MediaId = asset.Id,
                                    InspectionItemId = asset.InspectionItemId,
                                    ContentType = asset.ContentType,
                                    LengthBytes = asset.LengthBytes,
                                    Caption = asset.Caption,
                                    UploadedAtUtc = asset.UploadedAtUtc,
                                })
                                .ToList()
                            : Array.Empty<InspectionMediaItem>(),
                    })
                    .ToList(),
            })
            .ToList();

        return new InspectionDetails
        {
            InspectionId = row.Inspection.Id,
            RepairOrderId = row.Inspection.RepairOrderId,
            RepairOrderNumber = row.RepairOrder.Number,
            Status = row.Inspection.Status,
            CustomerDisplayName = row.Customer.DisplayName,
            VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            WorkshopLocationName = row.Location.Name,
            Sections = sections,
            Summary = summary,
            StartedAtUtc = row.Inspection.StartedAtUtc,
            CompletedAtUtc = row.Inspection.CompletedAtUtc,
            CreatedAtUtc = row.Inspection.CreatedAtUtc,
            UpdatedAtUtc = row.Inspection.UpdatedAtUtc,
            CanStart = canStart,
            CanUpdateItems = canUpdateItems,
            CanComplete = canComplete,
            CanUploadMedia = canMutateMedia,
            CanRemoveMedia = canMutateMedia,
            ActivePhotoCount = mediaAssets.Count,
        };
    }

    private static InspectionResultSummary BuildSummary(IReadOnlyList<InspectionItem> items) =>
        new()
        {
            Total = items.Count,
            Inspected = items.Count(item => InspectionLifecyclePolicy.IsInspected(item.Condition)),
            Good = items.Count(item => item.Condition == InspectionCondition.Good),
            Attention = items.Count(item => item.Condition == InspectionCondition.Attention),
            Critical = items.Count(item => item.Condition == InspectionCondition.Critical),
            Monitor = items.Count(item => item.Condition == InspectionCondition.Monitor),
        };

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

    private async Task<StaffMember?> ResolveLinkedStaffMemberAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        return await _dbContext.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == actorUserId, cancellationToken);
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

    private static InspectionListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<InspectionListItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = InspectionListQuery.DefaultPageSize,
        };

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private sealed class InspectionProjectionRow
    {
        public required Inspection Inspection { get; init; }

        public required RepairOrder RepairOrder { get; init; }

        public required Domain.Organizations.WorkshopLocation Location { get; init; }

        public required Domain.Customers.Customer Customer { get; init; }

        public required Domain.Vehicles.Vehicle Vehicle { get; init; }
    }
}
