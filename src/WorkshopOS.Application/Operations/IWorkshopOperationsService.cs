using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;

namespace WorkshopOS.Application.Operations;

public interface IWorkshopOperationsService
{
    Task<OperationsBoardResult> GetOperationsBoardAsync(
        OperationsBoardQuery query,
        CancellationToken cancellationToken = default);

    Task<RepairOrderOperationsContext?> GetRepairOrderOperationsContextAsync(
        Guid repairOrderId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<MyWorkResult> GetMyWorkAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<WorkshopOperationResult> AssignTechnicianAsync(
        Guid actorUserId,
        AssignTechnicianCommand command,
        CancellationToken cancellationToken = default);

    Task<WorkshopOperationResult> ReassignTechnicianAsync(
        Guid actorUserId,
        ReassignTechnicianCommand command,
        CancellationToken cancellationToken = default);

    Task<WorkshopOperationResult> UnassignTechnicianAsync(
        Guid actorUserId,
        UnassignTechnicianCommand command,
        CancellationToken cancellationToken = default);

    Task<WorkshopOperationResult> ChangeRepairOrderPriorityAsync(
        Guid actorUserId,
        ChangeRepairOrderPriorityCommand command,
        CancellationToken cancellationToken = default);

    Task<WorkshopOperationResult> StartAssignedWorkAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default);

    Task<WorkshopOperationResult> CompleteAssignedWorkAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken = default);
}

public sealed record OperationsBoardQuery
{
    public const int MaxResults = 200;

    public Guid? WorkshopLocationId { get; init; }

    public RepairOrderStatus? Status { get; init; }

    public RepairOrderPriority? Priority { get; init; }

    public Guid? AssignedStaffMemberId { get; init; }

    public bool UnassignedOnly { get; init; }

    public string? Search { get; init; }
}

public sealed class OperationsBoardResult
{
    public required OperationsBoardSummary Summary { get; init; }

    public required IReadOnlyList<OperationsBoardItem> Items { get; init; }

    public required int TotalMatchingCount { get; init; }

    public required bool ResultsTruncated { get; init; }
}

public sealed class OperationsBoardSummary
{
    public required int ActiveJobs { get; init; }

    public required int Unassigned { get; init; }

    public required int InProgress { get; init; }

    public required int Urgent { get; init; }
}

public sealed class OperationsBoardItem
{
    public required Guid RepairOrderId { get; init; }

    public required string Number { get; init; }

    public required RepairOrderStatus Status { get; init; }

    public required RepairOrderPriority Priority { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required DateTimeOffset OpenedAtUtc { get; init; }

    public Guid? AssignedStaffMemberId { get; init; }

    public string? AssignedTechnicianDisplayName { get; init; }

    public bool AssignedTechnicianIsInactive { get; init; }

    public TechnicianWorkStatus? TechnicianWorkStatus { get; init; }

    public InspectionStatus? LatestInspectionStatus { get; init; }

    public EstimateStatus? LatestEstimateStatus { get; init; }
}

public sealed class RepairOrderOperationsContext
{
    public required Guid RepairOrderId { get; init; }

    public required RepairOrderPriority Priority { get; init; }

    public required RepairOrderStatus Status { get; init; }

    public Guid? ActiveAssignmentId { get; init; }

    public Guid? AssignedStaffMemberId { get; init; }

    public string? AssignedTechnicianDisplayName { get; init; }

    public bool AssignedTechnicianIsInactive { get; init; }

    public TechnicianWorkStatus? TechnicianWorkStatus { get; init; }

    public required IReadOnlyList<AssignmentHistoryItem> AssignmentHistory { get; init; }

    public required IReadOnlyList<EligibleTechnicianOption> EligibleTechnicians { get; init; }

    public bool CanManageOperations { get; init; }

    public bool CanAssign { get; init; }

    public bool CanReassign { get; init; }

    public bool CanUnassign { get; init; }

    public bool CanChangePriority { get; init; }

    public bool CanStartOwnWork { get; init; }

    public bool CanCompleteOwnWork { get; init; }
}

public sealed class AssignmentHistoryItem
{
    public required Guid AssignmentId { get; init; }

    public required string TechnicianDisplayName { get; init; }

    public required TechnicianWorkStatus WorkStatus { get; init; }

    public required DateTimeOffset AssignedAtUtc { get; init; }

    public DateTimeOffset? UnassignedAtUtc { get; init; }
}

public sealed class EligibleTechnicianOption
{
    public required Guid StaffMemberId { get; init; }

    public required string DisplayName { get; init; }
}

public sealed class MyWorkResult
{
    public bool HasLinkedTechnicianProfile { get; init; }

    public required IReadOnlyList<MyWorkItem> Items { get; init; }
}

public sealed class MyWorkItem
{
    public required Guid RepairOrderId { get; init; }

    public required string Number { get; init; }

    public required RepairOrderPriority Priority { get; init; }

    public required RepairOrderStatus RepairOrderStatus { get; init; }

    public required TechnicianWorkStatus TechnicianWorkStatus { get; init; }

    public required string CustomerDisplayName { get; init; }

    public required string VehicleSummary { get; init; }

    public required string WorkshopLocationName { get; init; }

    public bool CanStartWork { get; init; }

    public bool CanCompleteWork { get; init; }

    public Guid? LatestInspectionId { get; init; }

    public InspectionStatus? LatestInspectionStatus { get; init; }

    public EstimateStatus? LatestEstimateStatus { get; init; }
}

public sealed record AssignTechnicianCommand
{
    public required Guid RepairOrderId { get; init; }

    public required Guid StaffMemberId { get; init; }
}

public sealed record ReassignTechnicianCommand
{
    public required Guid RepairOrderId { get; init; }

    public required Guid NewStaffMemberId { get; init; }
}

public sealed record UnassignTechnicianCommand
{
    public required Guid RepairOrderId { get; init; }
}

public sealed record ChangeRepairOrderPriorityCommand
{
    public required Guid RepairOrderId { get; init; }

    public required RepairOrderPriority Priority { get; init; }
}

public sealed class WorkshopOperationResult
{
    public bool Success { get; init; }

    public WorkshopOperationFailureReason? FailureReason { get; init; }

    public static WorkshopOperationResult Succeeded() => new() { Success = true };

    public static WorkshopOperationResult Failed(WorkshopOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum WorkshopOperationFailureReason
{
    OrganizationUnresolved = 1,
    RepairOrderNotFound = 2,
    StaffMemberNotFound = 3,
    AssignmentNotFound = 4,
    Unauthorized = 5,
    InvalidInput = 6,
    TerminalRepairOrder = 7,
    TechnicianNotEligible = 8,
    LocationMismatch = 9,
    DuplicateActiveAssignment = 10,
    InvalidWorkTransition = 11,
    TechnicianProfileNotLinked = 12,
    MembershipInactive = 13,
    StaffInactive = 14,
}

public static class WorkshopOperationsPolicy
{
    public static bool CanManageOperations(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;

    public static bool IsActiveBoardStatus(RepairOrderStatus status) =>
        status is not RepairOrderStatus.Completed and not RepairOrderStatus.Cancelled;

    public static bool IsEligibleTechnician(StaffMember staffMember) =>
        staffMember.Status == StaffStatus.Active
        && staffMember.Position == StaffPosition.Technician;

    public static bool CanStartTechnicianWork(TechnicianWorkStatus status) =>
        status is TechnicianWorkStatus.Assigned;

    public static bool CanCompleteTechnicianWork(TechnicianWorkStatus status) =>
        status is TechnicianWorkStatus.InProgress;
}
