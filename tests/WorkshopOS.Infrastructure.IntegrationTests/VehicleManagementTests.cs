using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Infrastructure.Vehicles;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class VehicleManagementServiceTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task VehicleManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext);

        var result = await service.CreateVehicleAsync(new CreateVehicleCommand
        {
            Make = "BMW",
            Model = "320i",
            ModelYear = 2020,
            Vin = suffix[..10],
            RegistrationPlate = $"34{suffix[..4]}",
            CurrentCustomerId = customer.Id,
        });

        Assert.True(result.Success);
        var vehicle = await writeScope.Vehicles.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(organization.Id, vehicle.OrganizationId);
        Assert.Equal(customer.Id, vehicle.CurrentCustomerId);
        Assert.Equal(suffix[..10].ToUpperInvariant(), vehicle.Vin);
    }

    [Fact]
    public async Task VehicleManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = CreateService(scope.Context, new UnresolvedOrganizationContext());

        var result = await service.CreateVehicleAsync(new CreateVehicleCommand
        {
            Make = "BMW",
            Model = "320i",
        });

        Assert.False(result.Success);
        Assert.Equal(VehicleOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.Vehicles.CountAsync());
    }

    [Fact]
    public async Task VehicleManagement_List_ReturnsOnlyCurrentOrganizationVehicles()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, $"{suffix}-a", make: "Audi");
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, $"{suffix}-b", make: "Volvo");
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await service.ListVehiclesAsync(new VehicleListQuery());

        Assert.Single(result.Items);
        Assert.Equal("Audi", result.Items[0].Make);
    }

    [Fact]
    public async Task VehicleManagement_Search_DoesNotReturnOtherTenantVehicles()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var sharedPlate = $"P{suffix[..8]}";

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            await TestDataFactory.PersistVehicleAsync(
                scopeA,
                organizationA.Id,
                $"{suffix}-a",
                registrationPlate: sharedPlate,
                make: "Tenant A Make");
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            await TestDataFactory.PersistVehicleAsync(
                scopeB,
                organizationB.Id,
                $"{suffix}-b",
                registrationPlate: sharedPlate,
                make: "Tenant B Make");
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await service.ListVehiclesAsync(new VehicleListQuery { Search = sharedPlate });

        Assert.Single(result.Items);
        Assert.Equal("Tenant A Make", result.Items[0].Make);
    }

    [Fact]
    public async Task VehicleManagement_GetDetails_DoesNotExposeOtherTenantVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid vehicleBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            vehicleBId = (await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var details = await service.GetVehicleDetailsAsync(vehicleBId);

        Assert.Null(details);
    }

    [Fact]
    public async Task VehicleManagement_Update_UpdatesSameTenantVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext);

        var result = await service.UpdateVehicleAsync(new UpdateVehicleCommand
        {
            VehicleId = vehicle.Id,
            Make = "Mercedes-AMG",
            Model = "C63",
            ModelYear = 2019,
            Vin = "updatedvin",
            RegistrationPlate = "updatedplate",
            Color = "Black",
        });

        Assert.True(result.Success);
        var updated = await writeScope.Vehicles.SingleAsync(candidate => candidate.Id == vehicle.Id);
        Assert.Equal("Mercedes-AMG", updated.Make);
        Assert.Equal("UPDATEDVIN", updated.Vin);
        Assert.Equal("UPDATEDPLATE", updated.RegistrationPlate);
    }

    [Fact]
    public async Task VehicleManagement_Update_DoesNotModifyOtherTenantVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid vehicleBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            vehicleBId = (await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id));

        var result = await service.UpdateVehicleAsync(new UpdateVehicleCommand
        {
            VehicleId = vehicleBId,
            Make = "Cross tenant",
            Model = "Update",
        });

        Assert.False(result.Success);
        Assert.Equal(VehicleOperationFailureReason.VehicleNotFound, result.FailureReason);
    }

    [Fact]
    public async Task VehicleManagement_ReassignCustomer_UpdatesCurrentCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var customerA = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-a");
        var customerB = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-b");
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customerA.Id);
        var service = CreateService(writeScope, organizationContext);

        var result = await service.ReassignVehicleCustomerAsync(new ReassignVehicleCustomerCommand
        {
            VehicleId = vehicle.Id,
            NewCurrentCustomerId = customerB.Id,
        });

        Assert.True(result.Success);
        var updated = await writeScope.Vehicles.SingleAsync(candidate => candidate.Id == vehicle.Id);
        Assert.Equal(customerB.Id, updated.CurrentCustomerId);
    }

    [Fact]
    public async Task VehicleManagement_ReassignCustomer_DoesNotRewriteHistoricalRepairOrderCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAtUtc = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-a");
        var customerB = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-b");
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customerA.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customerA.Id,
            vehicle.Id,
            suffix,
            openedAtUtc);

        var service = CreateService(writeScope, organizationContext);
        var result = await service.ReassignVehicleCustomerAsync(new ReassignVehicleCustomerCommand
        {
            VehicleId = vehicle.Id,
            NewCurrentCustomerId = customerB.Id,
        });

        Assert.True(result.Success);

        var updatedVehicle = await writeScope.Vehicles.SingleAsync(candidate => candidate.Id == vehicle.Id);
        var historicalRepairOrder = await writeScope.RepairOrders.SingleAsync(candidate => candidate.Id == repairOrder.Id);

        Assert.Equal(customerB.Id, updatedVehicle.CurrentCustomerId);
        Assert.Equal(customerA.Id, historicalRepairOrder.CustomerId);
    }

    [Fact]
    public async Task VehicleManagement_ReassignCustomer_RejectsOtherTenantCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid customerBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            customerBId = (await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScopeA, organizationA.Id, suffix);
        var service = CreateService(writeScopeA, new TestOrganizationContext(organizationA.Id));

        var result = await service.ReassignVehicleCustomerAsync(new ReassignVehicleCustomerCommand
        {
            VehicleId = vehicle.Id,
            NewCurrentCustomerId = customerBId,
        });

        Assert.False(result.Success);
        Assert.Equal(VehicleOperationFailureReason.CustomerNotFound, result.FailureReason);
    }

    [Fact]
    public async Task VehicleManagement_ClientCannotChooseOrganizationId()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var organizationContext = new TestOrganizationContext(organizationA.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        writeScope.Vehicles.Add(new Vehicle(organizationB.Id, "Cross", "Tenant"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writeScope.SaveChangesAsync());
        Assert.Contains("belong to the resolved organization", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VehicleManagement_List_FiltersByCurrentCustomerId()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var customerA = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-a");
        var customerB = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, $"{suffix}-b");
        await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, $"{suffix}-1", customerA.Id);
        await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, $"{suffix}-2", customerB.Id);
        await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, $"{suffix}-3", currentCustomerId: null);

        var service = CreateService(writeScope, organizationContext);
        var result = await service.ListVehiclesAsync(new VehicleListQuery { CurrentCustomerId = customerA.Id });

        Assert.Single(result.Items);
    }

    private static VehicleManagementService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext) =>
        new(context, organizationContext, TimeProvider.System);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class VehicleManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task VehicleManager_AllowsOwner()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);
    }

    [Fact]
    public async Task VehicleManager_AllowsAdministrator()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);
    }

    [Fact]
    public async Task VehicleManager_AllowsServiceAdvisor()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);
    }

    [Fact]
    public async Task VehicleManager_RejectsTechnician()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);
    }

    [Fact]
    public async Task VehicleManager_RejectsViewer()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);
    }

    [Fact]
    public async Task VehicleManager_ReflectsDatabaseRoleChange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var advisor = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-advisor");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            owner.Id,
            OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            advisor.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var handler = new VehicleManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new VehicleManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new VehicleManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(afterContext);
        Assert.False(afterContext.HasSucceeded);
    }

    private async Task AssertRoleAllowed(OrganizationMembershipRole role, bool shouldSucceed)
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var user = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, user.Id, role);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var handler = new VehicleManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new VehicleManagerRequirement()],
            CreatePrincipal(user.Id),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
    }

    private static System.Security.Claims.ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Test"));
}
