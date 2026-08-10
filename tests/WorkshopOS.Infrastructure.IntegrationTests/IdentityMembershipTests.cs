using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class IdentityMembershipModelTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void ApplicationUser_HasNoOrganizationId()
    {
        Assert.Null(typeof(ApplicationUser).GetProperty("OrganizationId", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void OrganizationMembership_IsNotTenantQueryFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        var entityType = context.Model.FindEntityType(typeof(OrganizationMembership));
        Assert.NotNull(entityType);
        Assert.Empty(entityType.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task OrganizationMembership_RequiresUniqueOrganizationUserPair()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        scope.Context.OrganizationMemberships.Add(
            new OrganizationMembership(organization.Id, user.Id, OrganizationMembershipRole.Owner));
        await scope.Context.SaveChangesAsync();

        scope.Context.ChangeTracker.Clear();
        scope.Context.OrganizationMemberships.Add(
            new OrganizationMembership(organization.Id, user.Id, OrganizationMembershipRole.Viewer));

        await Assert.ThrowsAsync<DbUpdateException>(() => scope.Context.SaveChangesAsync());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class OrganizationResolutionTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task OrganizationResolution_NoActiveMembership_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        var resolver = new OrganizationResolutionService(scope.Context);

        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_OneActiveMembership_ResolvesOrganization()
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

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id);

        Assert.Equal(organization.Id, organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_SuspendedMembership_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            user.Id,
            OrganizationMembershipRole.Technician,
            OrganizationMembershipStatus.Suspended);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_MultipleMembershipsWithoutSelection_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            user.Id,
            OrganizationMembershipRole.Viewer);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_MultipleMembershipsWithValidSelection_ResolvesSelectedOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            user.Id,
            OrganizationMembershipRole.Viewer);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id, organizationB.Id);

        Assert.Equal(organizationB.Id, organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_SelectedOrganizationWithoutMembership_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var outsider = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-outsider");
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            user.Id,
            OrganizationMembershipRole.Viewer);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id, outsider.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_SelectedSuspendedMembership_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            user.Id,
            OrganizationMembershipRole.Viewer,
            OrganizationMembershipStatus.Suspended);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id, organizationB.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_SelectedInactiveOrganization_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var activeOrganization = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-active");
        var inactiveOrganization = new Organization(
            $"Organization {suffix}-inactive",
            $"org-{suffix}-inactive",
            "TRY",
            "Europe/Istanbul",
            OrganizationStatus.Suspended);
        scope.Context.Organizations.Add(inactiveOrganization);
        await scope.Context.SaveChangesAsync();

        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            activeOrganization.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            inactiveOrganization.Id,
            user.Id,
            OrganizationMembershipRole.Viewer);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id, inactiveOrganization.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_MultipleActiveMemberships_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            user.Id,
            OrganizationMembershipRole.Viewer);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id);

        Assert.Null(organizationId);
    }

    [Fact]
    public async Task OrganizationResolution_InactiveOrganization_RemainsUnresolved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = new Organization(
            $"Organization {suffix}",
            $"org-{suffix}",
            "TRY",
            "Europe/Istanbul",
            OrganizationStatus.Suspended);
        scope.Context.Organizations.Add(organization);
        await scope.Context.SaveChangesAsync();

        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            user.Id,
            OrganizationMembershipRole.Owner);

        var resolver = new OrganizationResolutionService(scope.Context);
        var organizationId = await resolver.ResolveOrganizationIdAsync(user.Id);

        Assert.Null(organizationId);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class AuthorizationPolicyTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task OrganizationMemberPolicy_RejectsUnresolvedContext()
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

        var handler = new OrganizationMemberAuthorizationHandler(
            scope.Context,
            new UnresolvedOrganizationContext());

        var context = new AuthorizationHandlerContext(
            [new OrganizationMemberRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task OrganizationMemberPolicy_AllowsActiveResolvedMembership()
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

        var organizationContext = new TestOrganizationContext(organization.Id);
        var handler = new OrganizationMemberAuthorizationHandler(scope.Context, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new OrganizationMemberRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task OrganizationManagerPolicy_AllowsOwnerOrAdministrator()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var administrator = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-admin");

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            owner.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            administrator.Id,
            OrganizationMembershipRole.Administrator);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var handler = new OrganizationManagerAuthorizationHandler(scope.Context, organizationContext);

        foreach (var userId in new[] { owner.Id, administrator.Id })
        {
            var context = new AuthorizationHandlerContext(
                [new OrganizationManagerRequirement()],
                CreatePrincipal(userId),
                resource: null);

            await handler.HandleAsync(context);
            Assert.True(context.HasSucceeded);
        }
    }

    [Fact]
    public async Task OrganizationManagerPolicy_RejectsTechnicianOrViewer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technician = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");
        var viewer = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-viewer");

        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            technician.Id,
            OrganizationMembershipRole.Technician);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            viewer.Id,
            OrganizationMembershipRole.Viewer);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var handler = new OrganizationManagerAuthorizationHandler(scope.Context, organizationContext);

        foreach (var userId in new[] { technician.Id, viewer.Id })
        {
            var context = new AuthorizationHandlerContext(
                [new OrganizationManagerRequirement()],
                CreatePrincipal(userId),
                resource: null);

            await handler.HandleAsync(context);
            Assert.False(context.HasSucceeded);
        }
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                authenticationType: "Test"));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class IdentityAuthenticationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task IdentityPasswordHash_VerifiesValidAndRejectsInvalidPassword()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var userManager = IdentityTestServiceFactory.CreateUserManager(scope.Context);
        var user = new ApplicationUser
        {
            UserName = $"user-{suffix}@example.com",
            Email = $"user-{suffix}@example.com",
            EmailConfirmed = true,
            DisplayName = $"User {suffix}"
        };

        var createResult = await userManager.CreateAsync(user, IdentityTestServiceFactory.ValidTestPassword);
        Assert.True(createResult.Succeeded);

        Assert.True(await userManager.CheckPasswordAsync(user, IdentityTestServiceFactory.ValidTestPassword));
        Assert.False(await userManager.CheckPasswordAsync(user, "WrongPass123!"));
    }

    [Fact]
    public async Task LoginInfrastructure_CheckPasswordSignIn_SucceedsAndFails()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var userManager = IdentityTestServiceFactory.CreateUserManager(scope.Context);
        var signInManager = IdentityTestServiceFactory.CreateSignInManager(scope.Context);
        var user = new ApplicationUser
        {
            UserName = $"login-{suffix}@example.com",
            Email = $"login-{suffix}@example.com",
            EmailConfirmed = true,
            DisplayName = $"Login {suffix}"
        };

        var createResult = await userManager.CreateAsync(user, IdentityTestServiceFactory.ValidTestPassword);
        Assert.True(createResult.Succeeded);

        var validSignIn = await signInManager.CheckPasswordSignInAsync(
            user,
            IdentityTestServiceFactory.ValidTestPassword,
            lockoutOnFailure: true);
        Assert.True(validSignIn.Succeeded);

        var invalidSignIn = await signInManager.CheckPasswordSignInAsync(
            user,
            "WrongPass123!",
            lockoutOnFailure: true);
        Assert.False(invalidSignIn.Succeeded);
    }
}
