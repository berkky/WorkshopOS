using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Onboarding;
using WorkshopOS.Application.Organizations;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OwnerOnboardingTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task OwnerOnboarding_ValidInput_CreatesCompleteOwnerWorkspace()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();

        await using var scope = await fixture.BeginScopeAsync(organizationContext);
        var service = OwnerOnboardingTestServiceFactory.Create(scope.Context, organizationContext);
        var command = OwnerOnboardingTestServiceFactory.CreateValidCommand(suffix);

        var result = await service.OnboardOwnerAsync(command);

        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.NotEqual(Guid.Empty, result.OrganizationId);
        Assert.NotEqual(Guid.Empty, result.WorkshopLocationId);

        var user = await scope.Context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == result.UserId);
        Assert.Equal(command.Email.Trim(), user.Email);
        Assert.False(user.EmailConfirmed);

        var organization = await scope.Context.Organizations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == result.OrganizationId);
        Assert.Equal(command.OrganizationName.Trim(), organization.Name);
        Assert.Equal("EUR", organization.DefaultCurrencyCode);
        Assert.Equal(OrganizationStatus.Active, organization.Status);
        Assert.False(string.IsNullOrWhiteSpace(organization.Slug));

        var membership = await scope.Context.OrganizationMemberships.AsNoTracking()
            .SingleAsync(candidate =>
                candidate.OrganizationId == result.OrganizationId
                && candidate.UserId == result.UserId);
        Assert.Equal(OrganizationMembershipRole.Owner, membership.Role);
        Assert.Equal(OrganizationMembershipStatus.Active, membership.Status);

        var location = await scope.Context.WorkshopLocations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == result.WorkshopLocationId);
        Assert.Equal(result.OrganizationId, location.OrganizationId);
        Assert.True(location.IsActive);
        Assert.Equal(command.WorkshopLocationName.Trim(), location.Name);
    }

    [Fact]
    public async Task OwnerOnboarding_WhenFailureOccursAfterIdentityCreation_RollsBackEverything()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();
        var command = OwnerOnboardingTestServiceFactory.CreateValidCommand(suffix);

        await using (var context = fixture.CreateContext(organizationContext, TimeProvider.System))
        {
            var service = OwnerOnboardingTestServiceFactory.Create(
                context,
                organizationContext,
                new ThrowingOwnerOnboardingPostIdentityGate());

            var result = await service.OnboardOwnerAsync(command);

            Assert.False(result.Success);
            Assert.Equal(OwnerOnboardingFailureCategory.UnexpectedFailure, result.FailureCategory);
        }

        await using var verifyContext = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.False(await verifyContext.Users.AnyAsync(user => user.Email == command.Email));
        Assert.False(await verifyContext.Organizations.AnyAsync(organization => organization.Name == command.OrganizationName.Trim()));
        Assert.False(await verifyContext.OrganizationMemberships.AnyAsync());
        Assert.False(await verifyContext.WorkshopLocations.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task OwnerOnboarding_DuplicateEmail_DoesNotCreateOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();

        await using var scope = await fixture.BeginScopeAsync(organizationContext);
        var service = OwnerOnboardingTestServiceFactory.Create(scope.Context, organizationContext);
        var command = OwnerOnboardingTestServiceFactory.CreateValidCommand(suffix);

        var firstResult = await service.OnboardOwnerAsync(command);
        Assert.True(firstResult.Success);

        var duplicateResult = await service.OnboardOwnerAsync(command);

        Assert.False(duplicateResult.Success);
        Assert.Equal(OwnerOnboardingFailureCategory.DuplicateAccount, duplicateResult.FailureCategory);
        Assert.Equal(1, await scope.Context.Organizations.CountAsync());
        Assert.Equal(1, await scope.Context.OrganizationMemberships.CountAsync());
        Assert.Equal(1, await scope.Context.WorkshopLocations.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task OwnerOnboarding_InvalidTimeZone_DoesNotWrite()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();

        await using var scope = await fixture.BeginScopeAsync(organizationContext);
        var service = OwnerOnboardingTestServiceFactory.Create(scope.Context, organizationContext);
        var command = OwnerOnboardingTestServiceFactory.CreateCommandWithInvalidTimeZone(suffix);

        var result = await service.OnboardOwnerAsync(command);

        Assert.False(result.Success);
        Assert.Equal(OwnerOnboardingFailureCategory.InvalidInput, result.FailureCategory);
        Assert.Equal(0, await scope.Context.Users.CountAsync());
        Assert.Equal(0, await scope.Context.Organizations.CountAsync());
        Assert.Equal(0, await scope.Context.OrganizationMemberships.CountAsync());
        Assert.Equal(0, await scope.Context.WorkshopLocations.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task OwnerOnboarding_InvalidCurrencyFormat_DoesNotWrite()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();

        await using var scope = await fixture.BeginScopeAsync(organizationContext);
        var service = OwnerOnboardingTestServiceFactory.Create(scope.Context, organizationContext);
        var command = OwnerOnboardingTestServiceFactory.CreateCommandWithInvalidCurrency(suffix);

        var result = await service.OnboardOwnerAsync(command);

        Assert.False(result.Success);
        Assert.Equal(OwnerOnboardingFailureCategory.InvalidInput, result.FailureCategory);
        Assert.Equal(0, await scope.Context.Users.CountAsync());
        Assert.Equal(0, await scope.Context.Organizations.CountAsync());
        Assert.Equal(0, await scope.Context.OrganizationMemberships.CountAsync());
        Assert.Equal(0, await scope.Context.WorkshopLocations.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task OwnerOnboarding_CreatesOwnerActiveMembership()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();

        await using var scope = await fixture.BeginScopeAsync(organizationContext);
        var service = OwnerOnboardingTestServiceFactory.Create(scope.Context, organizationContext);
        var command = OwnerOnboardingTestServiceFactory.CreateValidCommand(suffix);

        var result = await service.OnboardOwnerAsync(command);

        Assert.True(result.Success);

        var membership = await scope.Context.OrganizationMemberships.AsNoTracking()
            .SingleAsync(candidate =>
                candidate.OrganizationId == result.OrganizationId
                && candidate.UserId == result.UserId);

        Assert.Equal(OrganizationMembershipRole.Owner, membership.Role);
        Assert.Equal(OrganizationMembershipStatus.Active, membership.Status);
    }

    [Fact]
    public async Task OwnerOnboarding_CreatesActiveFirstLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var organizationContext = new ScopedOrganizationContext();

        await using var scope = await fixture.BeginScopeAsync(organizationContext);
        var service = OwnerOnboardingTestServiceFactory.Create(scope.Context, organizationContext);
        var command = OwnerOnboardingTestServiceFactory.CreateValidCommand(suffix);

        var result = await service.OnboardOwnerAsync(command);

        Assert.True(result.Success);

        var location = await scope.Context.WorkshopLocations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == result.WorkshopLocationId);

        Assert.True(location.IsActive);
        Assert.Equal(result.OrganizationId, location.OrganizationId);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class OrganizationSelectionTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task OrganizationSelection_RejectsNonMemberOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-other");

        var service = new OrganizationSelectionService(scope.Context);
        var result = await service.SelectOrganizationAsync(user.Id, organization.Id);

        Assert.False(result.Success);
        Assert.Equal(OrganizationSelectionFailureReason.NotMember, result.FailureReason);
    }

    [Fact]
    public async Task OrganizationSelection_AllowsActiveMembership()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            user.Id,
            OrganizationMembershipRole.Technician);

        var service = new OrganizationSelectionService(scope.Context);
        var result = await service.SelectOrganizationAsync(user.Id, organization.Id);

        Assert.True(result.Success);
        Assert.Equal(organization.Id, result.OrganizationId);
    }
}
