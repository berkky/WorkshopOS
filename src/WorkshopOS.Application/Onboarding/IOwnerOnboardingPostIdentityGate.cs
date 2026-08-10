namespace WorkshopOS.Application.Onboarding;

/// <summary>
/// Production seam invoked after Identity user creation and before tenant workspace provisioning.
/// Allows controlled continuation checks without client-controlled identifiers.
/// </summary>
public interface IOwnerOnboardingPostIdentityGate
{
    Task EnsureCanContinueAsync(CancellationToken cancellationToken = default);
}
