namespace WorkshopOS.Application.Organizations;

public interface IOrganizationSelectionService
{
    Task<IReadOnlyList<OrganizationSelectionOption>> GetSelectableOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OrganizationSelectionResult> SelectOrganizationAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationSelectionOption
{
    public required Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public required string MembershipRole { get; init; }
}

public sealed class OrganizationSelectionResult
{
    public bool Success { get; init; }

    public Guid OrganizationId { get; init; }

    public OrganizationSelectionFailureReason? FailureReason { get; init; }

    public static OrganizationSelectionResult Succeeded(Guid organizationId) =>
        new() { Success = true, OrganizationId = organizationId };

    public static OrganizationSelectionResult Failed(OrganizationSelectionFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum OrganizationSelectionFailureReason
{
    NotMember = 1,
    MembershipInactive = 2,
    OrganizationInactive = 3,
}
