namespace WorkshopOS.Application.Onboarding;

public interface IOwnerOnboardingService
{
    Task<OwnerOnboardingResult> OnboardOwnerAsync(
        OwnerOnboardingCommand command,
        CancellationToken cancellationToken = default);
}
