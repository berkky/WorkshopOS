using Microsoft.AspNetCore.Identity;
using WorkshopOS.Application.Onboarding;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Onboarding;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class OwnerOnboardingTestServiceFactory
{
    public static OwnerOnboardingService Create(
        AppDbContext context,
        IOrganizationContextMutator organizationContextMutator,
        IOwnerOnboardingPostIdentityGate? postIdentityGate = null)
    {
        var userManager = IdentityTestServiceFactory.CreateUserManager(context);
        return new OwnerOnboardingService(
            context,
            userManager,
            new OrganizationSlugGenerator(),
            new TrustedOrganizationMembershipValidator(context),
            organizationContextMutator,
            postIdentityGate ?? new OwnerOnboardingPostIdentityGate());
    }

    public static OwnerOnboardingCommand CreateValidCommand(string suffix) =>
        new()
        {
            OwnerDisplayName = $"Owner {suffix}",
            Email = $"owner-{suffix}@example.com",
            Password = IdentityTestServiceFactory.ValidTestPassword,
            OrganizationName = $"Workshop {suffix}",
            WorkshopLocationName = $"Main {suffix}",
            WorkshopLocationCode = $"LOC{suffix[..Math.Min(6, suffix.Length)]}".ToUpperInvariant(),
            TimeZoneId = "Europe/Istanbul",
            CurrencyCode = "eur",
        };

    public static OwnerOnboardingCommand CreateCommandWithInvalidTimeZone(string suffix) =>
        CreateValidCommand(suffix) with
        {
            TimeZoneId = "Not/A_Real_TimeZone",
        };

    public static OwnerOnboardingCommand CreateCommandWithInvalidCurrency(string suffix) =>
        CreateValidCommand(suffix) with
        {
            CurrencyCode = "EURO",
        };
}

internal sealed class ThrowingOwnerOnboardingPostIdentityGate : IOwnerOnboardingPostIdentityGate
{
    public Task EnsureCanContinueAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Post-identity continuation blocked for test.");
}
