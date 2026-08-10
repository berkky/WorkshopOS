namespace WorkshopOS.Application.Onboarding;

public sealed class OwnerOnboardingResult
{
    public bool Success { get; init; }

    public Guid UserId { get; init; }

    public Guid OrganizationId { get; init; }

    public Guid WorkshopLocationId { get; init; }

    public OwnerOnboardingFailureCategory? FailureCategory { get; init; }

    public static OwnerOnboardingResult Succeeded(Guid userId, Guid organizationId, Guid workshopLocationId) =>
        new()
        {
            Success = true,
            UserId = userId,
            OrganizationId = organizationId,
            WorkshopLocationId = workshopLocationId,
        };

    public static OwnerOnboardingResult Failed(OwnerOnboardingFailureCategory category) =>
        new()
        {
            Success = false,
            FailureCategory = category,
        };
}
