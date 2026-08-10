using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Memberships;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Memberships;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Team;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class StaffTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void StaffMember_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(StaffMember)));
    }

    [Fact]
    public void StaffLocationAssignment_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(StaffLocationAssignment)));
    }

    [Fact]
    public async Task StaffMember_CrossTenantUserLink_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var userB = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            userB.Id,
            OrganizationMembershipRole.Technician);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.StaffMembers.Add(new StaffMember(
            organizationA.Id,
            "Cross tenant staff",
            StaffPosition.Technician,
            userId: userB.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task StaffLocationAssignment_CrossTenantLocation_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var staffMember = await TestDataFactory.PersistStaffMemberAsync(scopeA, organizationA.Id, suffix);

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);

        scopeA.StaffLocationAssignments.Add(
            new StaffLocationAssignment(organizationA.Id, staffMember.Id, locationB.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class TeamManagementServiceTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task TeamManagement_CreateStaff_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var service = new TeamManagementService(writeScope, organizationContext);

        var result = await service.CreateStaffMemberAsync(new CreateStaffMemberCommand
        {
            DisplayName = $"Technician {suffix}",
            Position = StaffPosition.Technician,
            WorkshopLocationIds = [location.Id],
        });

        Assert.True(result.Success);
        var staffMember = await writeScope.StaffMembers.SingleAsync(member => member.Id == result.Value);
        Assert.Equal(organization.Id, staffMember.OrganizationId);
    }

    [Fact]
    public async Task TeamManagement_RejectsLinkedUserFromAnotherOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var outsider = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            outsider.Id,
            OrganizationMembershipRole.Technician);

        var organizationContext = new TestOrganizationContext(organizationA.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new TeamManagementService(writeScope, organizationContext);

        var result = await service.CreateStaffMemberAsync(new CreateStaffMemberCommand
        {
            DisplayName = $"Staff {suffix}",
            Position = StaffPosition.Technician,
            LinkedUserId = outsider.Id,
        });

        Assert.False(result.Success);
        Assert.Equal(TeamManagementFailureReason.LinkedUserNotMember, result.FailureReason);
    }

    [Fact]
    public async Task TeamManagement_AssignsSameTenantWorkshopLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var service = new TeamManagementService(writeScope, organizationContext);

        var result = await service.CreateStaffMemberAsync(new CreateStaffMemberCommand
        {
            DisplayName = $"Staff {suffix}",
            Position = StaffPosition.ServiceAdvisor,
            WorkshopLocationIds = [location.Id],
        });

        Assert.True(result.Success);

        var assignment = await writeScope.StaffLocationAssignments.SingleAsync();
        Assert.Equal(location.Id, assignment.WorkshopLocationId);
        Assert.Equal(organization.Id, assignment.OrganizationId);
    }

    [Fact]
    public async Task TeamManagement_QueryDoesNotExposeOtherOrganizationStaff()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            await TestDataFactory.PersistStaffMemberAsync(scopeA, organizationA.Id, $"{suffix}-a");
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            await TestDataFactory.PersistStaffMemberAsync(scopeB, organizationB.Id, $"{suffix}-b");
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = new TeamManagementService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var team = await service.GetTeamListAsync();

        Assert.Single(team);
        Assert.Contains("a", team[0].DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class MembershipManagementServiceTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task MembershipRole_OwnerCanAssignTechnician()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var viewer = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-viewer");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            owner.Id,
            OrganizationMembershipRole.Owner);
        var viewerMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            viewer.Id,
            OrganizationMembershipRole.Viewer);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var result = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = owner.Id,
            MembershipId = viewerMembership.Id,
            NewRole = OrganizationMembershipRole.Technician,
        });

        Assert.True(result.Success);
        var updated = await writeScope.OrganizationMemberships.SingleAsync(m => m.Id == viewerMembership.Id);
        Assert.Equal(OrganizationMembershipRole.Technician, updated.Role);
    }

    [Fact]
    public async Task MembershipRole_AdministratorCannotGrantOwner()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var admin = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-admin");
        var viewer = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-viewer");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            admin.Id,
            OrganizationMembershipRole.Administrator);
        var viewerMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            viewer.Id,
            OrganizationMembershipRole.Viewer);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var result = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = admin.Id,
            MembershipId = viewerMembership.Id,
            NewRole = OrganizationMembershipRole.Owner,
        });

        Assert.False(result.Success);
        Assert.Equal(MembershipManagementFailureReason.ActorNotAuthorized, result.FailureReason);
    }

    [Fact]
    public async Task MembershipRole_AdministratorCannotModifyOwner()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var admin = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-admin");
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            admin.Id,
            OrganizationMembershipRole.Administrator);
        var ownerMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            owner.Id,
            OrganizationMembershipRole.Owner);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var result = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = admin.Id,
            MembershipId = ownerMembership.Id,
            NewRole = OrganizationMembershipRole.Viewer,
        });

        Assert.False(result.Success);
        Assert.Equal(MembershipManagementFailureReason.ActorNotAuthorized, result.FailureReason);
    }

    [Fact]
    public async Task MembershipRole_SelfMutationRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        var ownerMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            owner.Id,
            OrganizationMembershipRole.Owner);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var result = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = owner.Id,
            MembershipId = ownerMembership.Id,
            NewRole = OrganizationMembershipRole.Administrator,
        });

        Assert.False(result.Success);
        Assert.Equal(MembershipManagementFailureReason.SelfMutationRejected, result.FailureReason);
    }

    [Fact]
    public async Task MembershipRole_LastActiveOwnerCannotBeDemoted()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var ownerOne = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-1");
        var ownerTwo = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-2");
        var ownerOneMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerOne.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerTwo.Id,
            OrganizationMembershipRole.Owner);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var demoteFirstOwner = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = ownerOneMembership.Id,
            NewRole = OrganizationMembershipRole.Technician,
        });
        Assert.True(demoteFirstOwner.Success);

        var demoteLastOwner = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = ownerOneMembership.Id,
            NewRole = OrganizationMembershipRole.Viewer,
        });
        Assert.True(demoteLastOwner.Success);

        var soleOwnerMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == ownerTwo.Id);
        var demoteSoleOwner = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = soleOwnerMembership.Id,
            NewRole = OrganizationMembershipRole.Administrator,
        });

        Assert.False(demoteSoleOwner.Success);
        Assert.Equal(MembershipManagementFailureReason.SelfMutationRejected, demoteSoleOwner.FailureReason);
    }

    [Fact]
    public async Task MembershipStatus_LastActiveOwnerCannotBeSuspendedOrRevoked()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var ownerOne = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-1");
        var ownerTwo = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-2");
        var ownerOneMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerOne.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerTwo.Id,
            OrganizationMembershipRole.Owner);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var suspendFirstOwner = await service.ChangeStatusAsync(new ChangeMembershipStatusCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = ownerOneMembership.Id,
            NewStatus = OrganizationMembershipStatus.Suspended,
        });
        Assert.True(suspendFirstOwner.Success);

        var soleOwnerMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == ownerTwo.Id);
        var suspendSoleOwner = await service.ChangeStatusAsync(new ChangeMembershipStatusCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = soleOwnerMembership.Id,
            NewStatus = OrganizationMembershipStatus.Suspended,
        });
        Assert.False(suspendSoleOwner.Success);
        Assert.Equal(MembershipManagementFailureReason.SelfMutationRejected, suspendSoleOwner.FailureReason);

        var revokeSoleOwner = await service.ChangeStatusAsync(new ChangeMembershipStatusCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = soleOwnerMembership.Id,
            NewStatus = OrganizationMembershipStatus.Revoked,
        });
        Assert.False(revokeSoleOwner.Success);
        Assert.Equal(MembershipManagementFailureReason.SelfMutationRejected, revokeSoleOwner.FailureReason);
    }

    [Fact]
    public async Task MembershipRole_LastOwnerProtectedWhenDemotingSoleActiveOwner()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var ownerOne = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-1");
        var ownerTwo = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-2");
        var ownerOneMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerOne.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerTwo.Id,
            OrganizationMembershipRole.Owner,
            OrganizationMembershipStatus.Suspended);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var result = await service.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = ownerTwo.Id,
            MembershipId = ownerOneMembership.Id,
            NewRole = OrganizationMembershipRole.Technician,
        });

        Assert.False(result.Success);
        Assert.Equal(MembershipManagementFailureReason.ActorNotAuthorized, result.FailureReason);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class MembershipAuthorizationRegressionTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task MembershipRole_AuthorizationReflectsDatabaseRoleChange()
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
        var adminMembership = await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            administrator.Id,
            OrganizationMembershipRole.Administrator);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var managerHandler = new OrganizationManagerAuthorizationHandler(writeScope, organizationContext);
        var membershipService = new OrganizationMembershipManagementService(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new OrganizationManagerRequirement()],
            CreatePrincipal(administrator.Id),
            resource: null);
        await managerHandler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var demoteResult = await membershipService.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = owner.Id,
            MembershipId = adminMembership.Id,
            NewRole = OrganizationMembershipRole.Technician,
        });
        Assert.True(demoteResult.Success);

        var afterContext = new AuthorizationHandlerContext(
            [new OrganizationManagerRequirement()],
            CreatePrincipal(administrator.Id),
            resource: null);
        await managerHandler.HandleAsync(afterContext);
        Assert.False(afterContext.HasSucceeded);
    }

    private static System.Security.Claims.ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
}
