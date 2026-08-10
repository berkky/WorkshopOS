using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Customers;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Customers;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomerManagementServiceTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task CustomerManagement_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var service = new CustomerManagementService(writeScope, organizationContext, TimeProvider.System);

        var result = await service.CreateCustomerAsync(new CreateCustomerCommand
        {
            DisplayName = $"Customer {suffix}",
            Email = $"customer-{suffix}@example.com",
            Phone = "+90 555 000 0000",
        });

        Assert.True(result.Success);
        var customer = await writeScope.Customers.SingleAsync(candidate => candidate.Id == result.Value);
        Assert.Equal(organization.Id, customer.OrganizationId);
    }

    [Fact]
    public async Task CustomerManagement_Create_WhenOrganizationUnresolved_FailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = new CustomerManagementService(scope.Context, new UnresolvedOrganizationContext(), TimeProvider.System);

        var result = await service.CreateCustomerAsync(new CreateCustomerCommand
        {
            DisplayName = "Unresolved customer",
        });

        Assert.False(result.Success);
        Assert.Equal(CustomerOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.Customers.CountAsync());
    }

    [Fact]
    public async Task CustomerManagement_List_ReturnsOnlyCurrentOrganizationCustomers()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, $"{suffix}-a");
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, $"{suffix}-b");
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = new CustomerManagementService(readScopeA, new TestOrganizationContext(organizationA.Id), TimeProvider.System);
        var result = await service.ListCustomersAsync(new CustomerListQuery());

        Assert.Single(result.Items);
        Assert.Contains("a", result.Items[0].DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerManagement_Search_DoesNotReturnOtherTenantCustomers()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var sharedEmail = $"shared-{suffix}@example.com";

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            scopeA.Customers.Add(new Customer(organizationA.Id, $"Customer A {suffix}", sharedEmail));
            await scopeA.SaveChangesAsync();
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            scopeB.Customers.Add(new Customer(organizationB.Id, $"Customer B {suffix}", sharedEmail));
            await scopeB.SaveChangesAsync();
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = new CustomerManagementService(readScopeA, new TestOrganizationContext(organizationA.Id), TimeProvider.System);
        var result = await service.ListCustomersAsync(new CustomerListQuery { Search = sharedEmail });

        Assert.Single(result.Items);
        Assert.Contains("Customer A", result.Items[0].DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerManagement_GetDetails_DoesNotExposeOtherTenantCustomer()
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

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = new CustomerManagementService(readScopeA, new TestOrganizationContext(organizationA.Id), TimeProvider.System);
        var details = await service.GetCustomerDetailsAsync(customerBId);

        Assert.Null(details);
    }

    [Fact]
    public async Task CustomerManagement_Update_UpdatesSameTenantCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var service = new CustomerManagementService(writeScope, organizationContext, TimeProvider.System);

        var result = await service.UpdateCustomerAsync(new UpdateCustomerCommand
        {
            CustomerId = customer.Id,
            DisplayName = $"Updated {suffix}",
            Email = $"updated-{suffix}@example.com",
            Phone = "12345",
            Notes = "Updated notes",
            IsActive = true,
        });

        Assert.True(result.Success);
        var updated = await writeScope.Customers.SingleAsync(candidate => candidate.Id == customer.Id);
        Assert.Equal($"Updated {suffix}", updated.DisplayName);
        Assert.Equal($"updated-{suffix}@example.com", updated.Email);
    }

    [Fact]
    public async Task CustomerManagement_Update_DoesNotModifyOtherTenantCustomer()
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
        var service = new CustomerManagementService(writeScopeA, new TestOrganizationContext(organizationA.Id), TimeProvider.System);

        var result = await service.UpdateCustomerAsync(new UpdateCustomerCommand
        {
            CustomerId = customerBId,
            DisplayName = "Cross tenant update",
        });

        Assert.False(result.Success);
        Assert.Equal(CustomerOperationFailureReason.CustomerNotFound, result.FailureReason);
    }

    [Fact]
    public async Task CustomerManagement_ClientCannotChooseOrganizationId()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var organizationContext = new TestOrganizationContext(organizationA.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        writeScope.Customers.Add(new Customer(organizationB.Id, "Cross tenant customer"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writeScope.SaveChangesAsync());
        Assert.Contains("belong to the resolved organization", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerManagement_Deactivate_SameTenantOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var service = new CustomerManagementService(writeScope, organizationContext, TimeProvider.System);

        Assert.True((await service.DeactivateCustomerAsync(customer.Id)).Success);
        var deactivated = await writeScope.Customers.SingleAsync(candidate => candidate.Id == customer.Id);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task CustomerManagement_DeactivatedCustomer_RemainsHistoricalRecord()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var service = new CustomerManagementService(writeScope, organizationContext, TimeProvider.System);

        await service.DeactivateCustomerAsync(customer.Id);

        var details = await service.GetCustomerDetailsAsync(customer.Id);
        Assert.NotNull(details);
        Assert.False(details.IsActive);
        Assert.Equal(1, await writeScope.Customers.CountAsync());
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomerManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task CustomerManager_AllowsOwner()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);
    }

    [Fact]
    public async Task CustomerManager_AllowsAdministrator()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);
    }

    [Fact]
    public async Task CustomerManager_AllowsServiceAdvisor()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);
    }

    [Fact]
    public async Task CustomerManager_RejectsTechnician()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);
    }

    [Fact]
    public async Task CustomerManager_RejectsViewer()
    {
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);
    }

    [Fact]
    public async Task CustomerManager_ReflectsDatabaseRoleChange()
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
        var handler = new CustomerManagerAuthorizationHandler(writeScope, organizationContext);

        var initialContext = new AuthorizationHandlerContext(
            [new CustomerManagerRequirement()],
            CreatePrincipal(advisor.Id),
            resource: null);
        await handler.HandleAsync(initialContext);
        Assert.True(initialContext.HasSucceeded);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterContext = new AuthorizationHandlerContext(
            [new CustomerManagerRequirement()],
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
        var handler = new CustomerManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new CustomerManagerRequirement()],
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
