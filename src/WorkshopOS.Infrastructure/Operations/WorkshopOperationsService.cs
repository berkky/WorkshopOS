using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Operations;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Operations;

public sealed class WorkshopOperationsService : IWorkshopOperationsService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public WorkshopOperationsService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<OperationsBoardResult> GetOperationsBoardAsync(
        OperationsBoardQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyBoardResult();
        }

        var boardQuery = BuildBoardQuery(query);
        var totalMatchingCount = await boardQuery.CountAsync(cancellationToken);

        var items = await boardQuery
            .OrderByDescending(row => row.RepairOrder.Priority)
            .ThenBy(row => row.RepairOrder.OpenedAtUtc)
            .ThenBy(row => row.RepairOrder.Id)
            .Take(OperationsBoardQuery.MaxResults)
            .Select(row => new OperationsBoardItem
            {
                RepairOrderId = row.RepairOrder.Id,
                Number = row.RepairOrder.Number,
                Status = row.RepairOrder.Status,
                Priority = row.RepairOrder.Priority,
                CustomerDisplayName = row.Customer.DisplayName,
                VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
                WorkshopLocationName = row.Location.Name,
                OpenedAtUtc = row.RepairOrder.OpenedAtUtc,
                AssignedStaffMemberId = row.Assignment != null ? row.Assignment.StaffMemberId : null,
                AssignedTechnicianDisplayName = row.StaffMember != null ? row.StaffMember.DisplayName : null,
                AssignedTechnicianIsInactive = row.StaffMember != null && row.StaffMember.Status != StaffStatus.Active,
                TechnicianWorkStatus = row.Assignment != null ? row.Assignment.WorkStatus : null,
            })
            .ToListAsync(cancellationToken);

        await ApplyLatestInspectionStatusesAsync(items, cancellationToken);
        await ApplyLatestEstimateStatusesAsync(items, cancellationToken);

        var summary = await BuildBoardSummaryAsync(cancellationToken);

        return new OperationsBoardResult
        {
            Summary = summary,
            Items = items,
            TotalMatchingCount = totalMatchingCount,
            ResultsTruncated = totalMatchingCount > OperationsBoardQuery.MaxResults,
        };
    }

    public async Task<RepairOrderOperationsContext?> GetRepairOrderOperationsContextAsync(
        Guid repairOrderId,
        Guid actorUserId,
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

        var membershipRole = await ResolveMembershipRoleAsync(actorUserId, cancellationToken);
        var canManage = WorkshopOperationsPolicy.CanManageOperations(membershipRole);

        var activeAssignment = await _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.RepairOrderId == repairOrderId && candidate.UnassignedAtUtc == null,
                cancellationToken);

        StaffMember? assignedStaff = null;
        if (activeAssignment is not null)
        {
            assignedStaff = await _dbContext.StaffMembers.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == activeAssignment.StaffMemberId, cancellationToken);
        }

        var history = await (
            from assignment in _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            join staff in _dbContext.StaffMembers.AsNoTracking()
                on assignment.StaffMemberId equals staff.Id
            where assignment.RepairOrderId == repairOrderId
            orderby assignment.AssignedAtUtc descending
            select new AssignmentHistoryItem
            {
                AssignmentId = assignment.Id,
                TechnicianDisplayName = staff.DisplayName,
                WorkStatus = assignment.WorkStatus,
                AssignedAtUtc = assignment.AssignedAtUtc,
                UnassignedAtUtc = assignment.UnassignedAtUtc,
            }).ToListAsync(cancellationToken);

        var eligibleTechnicians = canManage && !RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status)
            ? await GetEligibleTechniciansAsync(repairOrder.WorkshopLocationId, cancellationToken)
            : Array.Empty<EligibleTechnicianOption>();

        var linkedStaff = await ResolveLinkedStaffMemberAsync(actorUserId, cancellationToken);
        var membershipActive = await IsMembershipActiveAsync(actorUserId, cancellationToken);
        var canTechnicianAct = linkedStaff is not null
                               && membershipActive
                               && linkedStaff.Status == StaffStatus.Active
                               && activeAssignment is not null
                               && activeAssignment.StaffMemberId == linkedStaff.Id
                               && !RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status);

        return new RepairOrderOperationsContext
        {
            RepairOrderId = repairOrder.Id,
            Priority = repairOrder.Priority,
            Status = repairOrder.Status,
            ActiveAssignmentId = activeAssignment?.Id,
            AssignedStaffMemberId = activeAssignment?.StaffMemberId,
            AssignedTechnicianDisplayName = assignedStaff?.DisplayName,
            AssignedTechnicianIsInactive = assignedStaff is not null && assignedStaff.Status != StaffStatus.Active,
            TechnicianWorkStatus = activeAssignment?.WorkStatus,
            AssignmentHistory = history,
            EligibleTechnicians = eligibleTechnicians,
            CanManageOperations = canManage,
            CanAssign = canManage && activeAssignment is null && !RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status),
            CanReassign = canManage && activeAssignment is not null && !RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status),
            CanUnassign = canManage && activeAssignment is not null && !RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status),
            CanChangePriority = canManage && !RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status),
            CanStartOwnWork = canTechnicianAct
                              && activeAssignment is not null
                              && WorkshopOperationsPolicy.CanStartTechnicianWork(activeAssignment.WorkStatus),
            CanCompleteOwnWork = canTechnicianAct
                                 && activeAssignment is not null
                                 && WorkshopOperationsPolicy.CanCompleteTechnicianWork(activeAssignment.WorkStatus),
        };
    }

    public async Task<MyWorkResult> GetMyWorkAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return new MyWorkResult { HasLinkedTechnicianProfile = false, Items = Array.Empty<MyWorkItem>() };
        }

        var linkedStaff = await ResolveLinkedStaffMemberAsync(actorUserId, cancellationToken);
        if (linkedStaff is null
            || linkedStaff.Position != StaffPosition.Technician
            || !await IsMembershipActiveAsync(actorUserId, cancellationToken))
        {
            return new MyWorkResult
            {
                HasLinkedTechnicianProfile = linkedStaff is not null && linkedStaff.Position == StaffPosition.Technician,
                Items = Array.Empty<MyWorkItem>(),
            };
        }

        var items = await (
            from assignment in _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            join repairOrder in _dbContext.RepairOrders.AsNoTracking()
                on assignment.RepairOrderId equals repairOrder.Id
            join location in _dbContext.WorkshopLocations.AsNoTracking()
                on repairOrder.WorkshopLocationId equals location.Id
            join customer in _dbContext.Customers.AsNoTracking()
                on repairOrder.CustomerId equals customer.Id
            join vehicle in _dbContext.Vehicles.AsNoTracking()
                on repairOrder.VehicleId equals vehicle.Id
            where assignment.StaffMemberId == linkedStaff.Id
                  && assignment.UnassignedAtUtc == null
                  && repairOrder.Status != RepairOrderStatus.Completed
                  && repairOrder.Status != RepairOrderStatus.Cancelled
            orderby repairOrder.Priority descending, repairOrder.OpenedAtUtc, repairOrder.Id
            select new MyWorkItem
            {
                RepairOrderId = repairOrder.Id,
                Number = repairOrder.Number,
                Priority = repairOrder.Priority,
                RepairOrderStatus = repairOrder.Status,
                TechnicianWorkStatus = assignment.WorkStatus,
                CustomerDisplayName = customer.DisplayName,
                VehicleSummary = FormatVehicleSummary(vehicle.Make, vehicle.Model, vehicle.ModelYear),
                WorkshopLocationName = location.Name,
                CanStartWork = WorkshopOperationsPolicy.CanStartTechnicianWork(assignment.WorkStatus),
                CanCompleteWork = WorkshopOperationsPolicy.CanCompleteTechnicianWork(assignment.WorkStatus),
            }).ToListAsync(cancellationToken);

        await ApplyLatestInspectionStatusesToMyWorkAsync(items, cancellationToken);
        await ApplyLatestEstimateStatusesToMyWorkAsync(items, cancellationToken);

        return new MyWorkResult
        {
            HasLinkedTechnicianProfile = true,
            Items = items,
        };
    }

    public async Task<WorkshopOperationResult> AssignTechnicianAsync(
        Guid actorUserId,
        AssignTechnicianCommand command,
        CancellationToken cancellationToken = default) =>
        await ExecuteManagerOperationAsync(
            actorUserId,
            cancellationToken,
            async () =>
            {
                var repairOrder = await LoadRepairOrderAsync(command.RepairOrderId, cancellationToken);
                if (repairOrder is null)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.RepairOrderNotFound);
                }

                if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TerminalRepairOrder);
                }

                if (await HasActiveAssignmentAsync(command.RepairOrderId, cancellationToken))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.DuplicateActiveAssignment);
                }

                var eligibility = await ValidateTechnicianEligibilityAsync(
                    command.StaffMemberId,
                    repairOrder.WorkshopLocationId,
                    cancellationToken);

                if (eligibility == TechnicianEligibilityFailure.StaffMemberNotFound)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.StaffMemberNotFound);
                }

                if (eligibility != TechnicianEligibilityFailure.None)
                {
                    return MapEligibilityFailure(eligibility);
                }

                var assignment = new RepairOrderTechnicianAssignment(
                    repairOrder.OrganizationId,
                    repairOrder.Id,
                    command.StaffMemberId,
                    _timeProvider.GetUtcNow());

                _dbContext.RepairOrderTechnicianAssignments.Add(assignment);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return WorkshopOperationResult.Succeeded();
            });

    public async Task<WorkshopOperationResult> ReassignTechnicianAsync(
        Guid actorUserId,
        ReassignTechnicianCommand command,
        CancellationToken cancellationToken = default) =>
        await ExecuteManagerOperationAsync(
            actorUserId,
            cancellationToken,
            async () =>
            {
                var repairOrder = await LoadRepairOrderAsync(command.RepairOrderId, cancellationToken);
                if (repairOrder is null)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.RepairOrderNotFound);
                }

                if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TerminalRepairOrder);
                }

                var activeAssignment = await LoadActiveAssignmentAsync(command.RepairOrderId, cancellationToken);
                if (activeAssignment is null)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.AssignmentNotFound);
                }

                var eligibility = await ValidateTechnicianEligibilityAsync(
                    command.NewStaffMemberId,
                    repairOrder.WorkshopLocationId,
                    cancellationToken);

                if (eligibility == TechnicianEligibilityFailure.StaffMemberNotFound)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.StaffMemberNotFound);
                }

                if (eligibility != TechnicianEligibilityFailure.None)
                {
                    return MapEligibilityFailure(eligibility);
                }

                var now = _timeProvider.GetUtcNow();
                activeAssignment.Unassign(now);

                var newAssignment = new RepairOrderTechnicianAssignment(
                    repairOrder.OrganizationId,
                    repairOrder.Id,
                    command.NewStaffMemberId,
                    now);

                _dbContext.RepairOrderTechnicianAssignments.Add(newAssignment);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return WorkshopOperationResult.Succeeded();
            });

    public async Task<WorkshopOperationResult> UnassignTechnicianAsync(
        Guid actorUserId,
        UnassignTechnicianCommand command,
        CancellationToken cancellationToken = default) =>
        await ExecuteManagerOperationAsync(
            actorUserId,
            cancellationToken,
            async () =>
            {
                var repairOrder = await LoadRepairOrderAsync(command.RepairOrderId, cancellationToken);
                if (repairOrder is null)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.RepairOrderNotFound);
                }

                if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TerminalRepairOrder);
                }

                var activeAssignment = await LoadActiveAssignmentAsync(command.RepairOrderId, cancellationToken);
                if (activeAssignment is null)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.AssignmentNotFound);
                }

                activeAssignment.Unassign(_timeProvider.GetUtcNow());
                await _dbContext.SaveChangesAsync(cancellationToken);
                return WorkshopOperationResult.Succeeded();
            });

    public async Task<WorkshopOperationResult> ChangeRepairOrderPriorityAsync(
        Guid actorUserId,
        ChangeRepairOrderPriorityCommand command,
        CancellationToken cancellationToken = default) =>
        await ExecuteManagerOperationAsync(
            actorUserId,
            cancellationToken,
            async () =>
            {
                var repairOrder = await LoadRepairOrderAsync(command.RepairOrderId, cancellationToken);
                if (repairOrder is null)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.RepairOrderNotFound);
                }

                if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TerminalRepairOrder);
                }

                try
                {
                    repairOrder.ChangePriority(command.Priority);
                }
                catch (InvalidOperationException)
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TerminalRepairOrder);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                return WorkshopOperationResult.Succeeded();
            });

    public async Task<WorkshopOperationResult> StartAssignedWorkAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default) =>
        await ExecuteTechnicianWorkAsync(
            actorUserId,
            repairOrderId,
            cancellationToken,
            (repairOrder, assignment) =>
            {
                if (!WorkshopOperationsPolicy.CanStartTechnicianWork(assignment.WorkStatus))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.InvalidWorkTransition);
                }

                if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TerminalRepairOrder);
                }

                var now = _timeProvider.GetUtcNow();
                assignment.StartWork(now);

                if (repairOrder.Status == RepairOrderStatus.Draft)
                {
                    repairOrder.StartWork();
                }

                return WorkshopOperationResult.Succeeded();
            });

    public async Task<WorkshopOperationResult> CompleteAssignedWorkAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default) =>
        await ExecuteTechnicianWorkAsync(
            actorUserId,
            repairOrderId,
            cancellationToken,
            (_, assignment) =>
            {
                if (!WorkshopOperationsPolicy.CanCompleteTechnicianWork(assignment.WorkStatus))
                {
                    return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.InvalidWorkTransition);
                }

                assignment.CompleteWork(_timeProvider.GetUtcNow());
                return WorkshopOperationResult.Succeeded();
            });

    private async Task<WorkshopOperationResult> ExecuteManagerOperationAsync(
        Guid actorUserId,
        CancellationToken cancellationToken,
        Func<Task<WorkshopOperationResult>> operation)
    {
        if (!TryGetOrganizationId(out _))
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.OrganizationUnresolved);
        }

        var membershipRole = await ResolveMembershipRoleAsync(actorUserId, cancellationToken);
        if (!WorkshopOperationsPolicy.CanManageOperations(membershipRole))
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.Unauthorized);
        }

        try
        {
            return await operation();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.DuplicateActiveAssignment);
        }
    }

    private async Task<WorkshopOperationResult> ExecuteTechnicianWorkAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken,
        Func<RepairOrder, RepairOrderTechnicianAssignment, WorkshopOperationResult> mutate)
    {
        if (!TryGetOrganizationId(out _))
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.OrganizationUnresolved);
        }

        if (!await IsMembershipActiveAsync(actorUserId, cancellationToken))
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.MembershipInactive);
        }

        var linkedStaff = await ResolveLinkedStaffMemberAsync(actorUserId, cancellationToken);
        if (linkedStaff is null || linkedStaff.Position != StaffPosition.Technician)
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TechnicianProfileNotLinked);
        }

        if (linkedStaff.Status != StaffStatus.Active)
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.StaffInactive);
        }

        var repairOrder = await LoadRepairOrderAsync(repairOrderId, cancellationToken);
        if (repairOrder is null)
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.RepairOrderNotFound);
        }

        var assignment = await LoadActiveAssignmentAsync(repairOrderId, cancellationToken);
        if (assignment is null || assignment.StaffMemberId != linkedStaff.Id)
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.Unauthorized);
        }

        try
        {
            var result = mutate(repairOrder, assignment);
            if (!result.Success)
            {
                return result;
            }
        }
        catch (InvalidOperationException)
        {
            return WorkshopOperationResult.Failed(WorkshopOperationFailureReason.InvalidWorkTransition);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return WorkshopOperationResult.Succeeded();
    }

    private IQueryable<BoardProjectionRow> BuildBoardQuery(OperationsBoardQuery query)
    {
        var repairOrders = from repairOrder in _dbContext.RepairOrders.AsNoTracking()
                           join location in _dbContext.WorkshopLocations.AsNoTracking()
                               on repairOrder.WorkshopLocationId equals location.Id
                           join customer in _dbContext.Customers.AsNoTracking()
                               on repairOrder.CustomerId equals customer.Id
                           join vehicle in _dbContext.Vehicles.AsNoTracking()
                               on repairOrder.VehicleId equals vehicle.Id
                           select new BoardProjectionRow
                           {
                               RepairOrder = repairOrder,
                               Location = location,
                               Customer = customer,
                               Vehicle = vehicle,
                           };

        repairOrders = repairOrders.Where(row =>
            row.RepairOrder.Status != RepairOrderStatus.Completed
            && row.RepairOrder.Status != RepairOrderStatus.Cancelled);

        if (query.WorkshopLocationId.HasValue)
        {
            repairOrders = repairOrders.Where(row =>
                row.RepairOrder.WorkshopLocationId == query.WorkshopLocationId.Value);
        }

        if (query.Status.HasValue)
        {
            repairOrders = repairOrders.Where(row => row.RepairOrder.Status == query.Status.Value);
        }

        if (query.Priority.HasValue)
        {
            repairOrders = repairOrders.Where(row => row.RepairOrder.Priority == query.Priority.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            repairOrders = repairOrders.Where(row =>
                EF.Functions.ILike(row.RepairOrder.Number, pattern)
                || EF.Functions.ILike(row.Customer.DisplayName, pattern)
                || EF.Functions.ILike(row.Vehicle.Make, pattern)
                || EF.Functions.ILike(row.Vehicle.Model, pattern)
                || (row.Vehicle.RegistrationPlate != null
                    && EF.Functions.ILike(row.Vehicle.RegistrationPlate, pattern)));
        }

        if (query.UnassignedOnly)
        {
            repairOrders = repairOrders.Where(row =>
                !_dbContext.RepairOrderTechnicianAssignments.Any(assignment =>
                    assignment.RepairOrderId == row.RepairOrder.Id
                    && assignment.UnassignedAtUtc == null));
        }

        if (query.AssignedStaffMemberId.HasValue)
        {
            var staffMemberId = query.AssignedStaffMemberId.Value;
            repairOrders = repairOrders.Where(row =>
                _dbContext.RepairOrderTechnicianAssignments.Any(assignment =>
                    assignment.RepairOrderId == row.RepairOrder.Id
                    && assignment.StaffMemberId == staffMemberId
                    && assignment.UnassignedAtUtc == null));
        }

        return from row in repairOrders
               join assignment in _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
                   on row.RepairOrder.Id equals assignment.RepairOrderId into assignments
               from assignment in assignments.Where(candidate => candidate.UnassignedAtUtc == null).DefaultIfEmpty()
               join staff in _dbContext.StaffMembers.AsNoTracking()
                   on assignment.StaffMemberId equals staff.Id into staffMembers
               from staff in staffMembers.DefaultIfEmpty()
               select new BoardProjectionRow
               {
                   RepairOrder = row.RepairOrder,
                   Location = row.Location,
                   Customer = row.Customer,
                   Vehicle = row.Vehicle,
                   Assignment = assignment,
                   StaffMember = staff,
               };
    }

    private async Task<OperationsBoardSummary> BuildBoardSummaryAsync(CancellationToken cancellationToken)
    {
        var activeRepairOrders = _dbContext.RepairOrders.AsNoTracking()
            .Where(repairOrder => repairOrder.Status != RepairOrderStatus.Completed
                                  && repairOrder.Status != RepairOrderStatus.Cancelled);

        var activeJobs = await activeRepairOrders.CountAsync(cancellationToken);
        var inProgress = await activeRepairOrders.CountAsync(
            repairOrder => repairOrder.Status == RepairOrderStatus.InProgress,
            cancellationToken);
        var urgent = await activeRepairOrders.CountAsync(
            repairOrder => repairOrder.Priority == RepairOrderPriority.Urgent,
            cancellationToken);

        var assignedRepairOrderIds = _dbContext.RepairOrderTechnicianAssignments.AsNoTracking()
            .Where(assignment => assignment.UnassignedAtUtc == null)
            .Select(assignment => assignment.RepairOrderId);

        var unassigned = await activeRepairOrders.CountAsync(
            repairOrder => !assignedRepairOrderIds.Contains(repairOrder.Id),
            cancellationToken);

        return new OperationsBoardSummary
        {
            ActiveJobs = activeJobs,
            Unassigned = unassigned,
            InProgress = inProgress,
            Urgent = urgent,
        };
    }

    private async Task<IReadOnlyList<EligibleTechnicianOption>> GetEligibleTechniciansAsync(
        Guid workshopLocationId,
        CancellationToken cancellationToken) =>
        await (
            from staff in _dbContext.StaffMembers.AsNoTracking()
            join assignment in _dbContext.StaffLocationAssignments.AsNoTracking()
                on staff.Id equals assignment.StaffMemberId
            where staff.Status == StaffStatus.Active
                  && staff.Position == StaffPosition.Technician
                  && assignment.WorkshopLocationId == workshopLocationId
            orderby staff.DisplayName
            select new EligibleTechnicianOption
            {
                StaffMemberId = staff.Id,
                DisplayName = staff.DisplayName,
            }).ToListAsync(cancellationToken);

    private async Task<RepairOrder?> LoadRepairOrderAsync(Guid repairOrderId, CancellationToken cancellationToken) =>
        await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

    private async Task<RepairOrderTechnicianAssignment?> LoadActiveAssignmentAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken) =>
        await _dbContext.RepairOrderTechnicianAssignments
            .SingleOrDefaultAsync(
                candidate => candidate.RepairOrderId == repairOrderId && candidate.UnassignedAtUtc == null,
                cancellationToken);

    private async Task<bool> HasActiveAssignmentAsync(Guid repairOrderId, CancellationToken cancellationToken) =>
        await _dbContext.RepairOrderTechnicianAssignments.AnyAsync(
            candidate => candidate.RepairOrderId == repairOrderId && candidate.UnassignedAtUtc == null,
            cancellationToken);

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

    private async Task<bool> IsMembershipActiveAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return false;
        }

        return await _dbContext.OrganizationMemberships.AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == actorUserId
                              && membership.OrganizationId == organizationId
                              && membership.Status == OrganizationMembershipStatus.Active,
                cancellationToken);
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

    private async Task<TechnicianEligibilityFailure> ValidateTechnicianEligibilityAsync(
        Guid staffMemberId,
        Guid workshopLocationId,
        CancellationToken cancellationToken)
    {
        var staffMember = await _dbContext.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == staffMemberId, cancellationToken);

        if (staffMember is null)
        {
            return TechnicianEligibilityFailure.StaffMemberNotFound;
        }

        if (staffMember.Status != StaffStatus.Active)
        {
            return TechnicianEligibilityFailure.InactiveStaff;
        }

        if (staffMember.Position != StaffPosition.Technician)
        {
            return TechnicianEligibilityFailure.NonTechnicianPosition;
        }

        var hasLocation = await _dbContext.StaffLocationAssignments.AsNoTracking()
            .AnyAsync(
                assignment => assignment.StaffMemberId == staffMemberId
                              && assignment.WorkshopLocationId == workshopLocationId,
                cancellationToken);

        return hasLocation ? TechnicianEligibilityFailure.None : TechnicianEligibilityFailure.LocationMismatch;
    }

    private static WorkshopOperationResult MapEligibilityFailure(TechnicianEligibilityFailure failure) =>
        failure switch
        {
            TechnicianEligibilityFailure.InactiveStaff => WorkshopOperationResult.Failed(
                WorkshopOperationFailureReason.StaffInactive),
            TechnicianEligibilityFailure.NonTechnicianPosition => WorkshopOperationResult.Failed(
                WorkshopOperationFailureReason.TechnicianNotEligible),
            TechnicianEligibilityFailure.LocationMismatch => WorkshopOperationResult.Failed(
                WorkshopOperationFailureReason.LocationMismatch),
            _ => WorkshopOperationResult.Failed(WorkshopOperationFailureReason.TechnicianNotEligible),
        };

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException && postgresException.SqlState is "23505")
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

    private async Task ApplyLatestInspectionStatusesAsync(
        List<OperationsBoardItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var repairOrderIds = items.Select(item => item.RepairOrderId).ToList();
        var inspections = await _dbContext.Inspections.AsNoTracking()
            .Where(candidate => repairOrderIds.Contains(candidate.RepairOrderId))
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.RepairOrderId, candidate.Status })
            .ToListAsync(cancellationToken);

        var latestByRepairOrder = new Dictionary<Guid, InspectionStatus>();
        foreach (var inspection in inspections)
        {
            latestByRepairOrder.TryAdd(inspection.RepairOrderId, inspection.Status);
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            items[index] = new OperationsBoardItem
            {
                RepairOrderId = item.RepairOrderId,
                Number = item.Number,
                Status = item.Status,
                Priority = item.Priority,
                CustomerDisplayName = item.CustomerDisplayName,
                VehicleSummary = item.VehicleSummary,
                WorkshopLocationName = item.WorkshopLocationName,
                OpenedAtUtc = item.OpenedAtUtc,
                AssignedStaffMemberId = item.AssignedStaffMemberId,
                AssignedTechnicianDisplayName = item.AssignedTechnicianDisplayName,
                AssignedTechnicianIsInactive = item.AssignedTechnicianIsInactive,
                TechnicianWorkStatus = item.TechnicianWorkStatus,
                LatestInspectionStatus = latestByRepairOrder.TryGetValue(item.RepairOrderId, out var inspectionStatus)
                    ? inspectionStatus
                    : null,
                LatestEstimateStatus = item.LatestEstimateStatus,
            };
        }
    }

    private async Task ApplyLatestEstimateStatusesAsync(
        List<OperationsBoardItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var repairOrderIds = items.Select(item => item.RepairOrderId).ToList();
        var estimates = await _dbContext.Estimates.AsNoTracking()
            .Where(candidate => repairOrderIds.Contains(candidate.RepairOrderId))
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.RepairOrderId, candidate.Status })
            .ToListAsync(cancellationToken);

        var latestByRepairOrder = new Dictionary<Guid, EstimateStatus>();
        foreach (var estimate in estimates)
        {
            latestByRepairOrder.TryAdd(estimate.RepairOrderId, estimate.Status);
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            items[index] = new OperationsBoardItem
            {
                RepairOrderId = item.RepairOrderId,
                Number = item.Number,
                Status = item.Status,
                Priority = item.Priority,
                CustomerDisplayName = item.CustomerDisplayName,
                VehicleSummary = item.VehicleSummary,
                WorkshopLocationName = item.WorkshopLocationName,
                OpenedAtUtc = item.OpenedAtUtc,
                AssignedStaffMemberId = item.AssignedStaffMemberId,
                AssignedTechnicianDisplayName = item.AssignedTechnicianDisplayName,
                AssignedTechnicianIsInactive = item.AssignedTechnicianIsInactive,
                TechnicianWorkStatus = item.TechnicianWorkStatus,
                LatestInspectionStatus = item.LatestInspectionStatus,
                LatestEstimateStatus = latestByRepairOrder.TryGetValue(item.RepairOrderId, out var estimateStatus)
                    ? estimateStatus
                    : null,
            };
        }
    }

    private async Task ApplyLatestInspectionStatusesToMyWorkAsync(
        List<MyWorkItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var repairOrderIds = items.Select(item => item.RepairOrderId).ToList();
        var inspections = await _dbContext.Inspections.AsNoTracking()
            .Where(candidate => repairOrderIds.Contains(candidate.RepairOrderId))
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.RepairOrderId, candidate.Status })
            .ToListAsync(cancellationToken);

        var latestByRepairOrder = new Dictionary<Guid, (Guid Id, InspectionStatus Status)>();
        foreach (var inspection in inspections)
        {
            latestByRepairOrder.TryAdd(inspection.RepairOrderId, (inspection.Id, inspection.Status));
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            Guid? latestInspectionId = null;
            InspectionStatus? latestInspectionStatus = null;
            if (latestByRepairOrder.TryGetValue(item.RepairOrderId, out var latest))
            {
                latestInspectionId = latest.Id;
                latestInspectionStatus = latest.Status;
            }

            items[index] = new MyWorkItem
            {
                RepairOrderId = item.RepairOrderId,
                Number = item.Number,
                Priority = item.Priority,
                RepairOrderStatus = item.RepairOrderStatus,
                TechnicianWorkStatus = item.TechnicianWorkStatus,
                CustomerDisplayName = item.CustomerDisplayName,
                VehicleSummary = item.VehicleSummary,
                WorkshopLocationName = item.WorkshopLocationName,
                CanStartWork = item.CanStartWork,
                CanCompleteWork = item.CanCompleteWork,
                LatestInspectionId = latestInspectionId,
                LatestInspectionStatus = latestInspectionStatus,
                LatestEstimateStatus = item.LatestEstimateStatus,
            };
        }
    }

    private async Task ApplyLatestEstimateStatusesToMyWorkAsync(
        List<MyWorkItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var repairOrderIds = items.Select(item => item.RepairOrderId).ToList();
        var estimates = await _dbContext.Estimates.AsNoTracking()
            .Where(candidate => repairOrderIds.Contains(candidate.RepairOrderId))
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.RepairOrderId, candidate.Status })
            .ToListAsync(cancellationToken);

        var latestByRepairOrder = new Dictionary<Guid, EstimateStatus>();
        foreach (var estimate in estimates)
        {
            latestByRepairOrder.TryAdd(estimate.RepairOrderId, estimate.Status);
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            items[index] = new MyWorkItem
            {
                RepairOrderId = item.RepairOrderId,
                Number = item.Number,
                Priority = item.Priority,
                RepairOrderStatus = item.RepairOrderStatus,
                TechnicianWorkStatus = item.TechnicianWorkStatus,
                CustomerDisplayName = item.CustomerDisplayName,
                VehicleSummary = item.VehicleSummary,
                WorkshopLocationName = item.WorkshopLocationName,
                CanStartWork = item.CanStartWork,
                CanCompleteWork = item.CanCompleteWork,
                LatestInspectionId = item.LatestInspectionId,
                LatestInspectionStatus = item.LatestInspectionStatus,
                LatestEstimateStatus = latestByRepairOrder.TryGetValue(item.RepairOrderId, out var estimateStatus)
                    ? estimateStatus
                    : null,
            };
        }
    }

    private static OperationsBoardResult EmptyBoardResult() =>
        new()
        {
            Summary = new OperationsBoardSummary
            {
                ActiveJobs = 0,
                Unassigned = 0,
                InProgress = 0,
                Urgent = 0,
            },
            Items = Array.Empty<OperationsBoardItem>(),
            TotalMatchingCount = 0,
            ResultsTruncated = false,
        };

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private enum TechnicianEligibilityFailure
    {
        None = 0,
        StaffMemberNotFound = 1,
        InactiveStaff = 2,
        NonTechnicianPosition = 3,
        LocationMismatch = 4,
    }

    private sealed class BoardProjectionRow
    {
        public required RepairOrder RepairOrder { get; init; }

        public required Domain.Organizations.WorkshopLocation Location { get; init; }

        public required Domain.Customers.Customer Customer { get; init; }

        public required Domain.Vehicles.Vehicle Vehicle { get; init; }

        public RepairOrderTechnicianAssignment? Assignment { get; init; }

        public StaffMember? StaffMember { get; init; }
    }
}
