using WorkshopOS.Application.Onboarding;

namespace WorkshopOS.Infrastructure.Onboarding;

public sealed class OwnerOnboardingPostIdentityGate : IOwnerOnboardingPostIdentityGate
{
    public Task EnsureCanContinueAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
