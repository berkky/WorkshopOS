using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.RepairOrders;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepairOrderPaginationTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset BaseOpenedAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RepairOrderList_DefaultPage_ReturnsFirstPageWithDefaultPageSize()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 25);

        var service = CreateService(scope.Context, scope.OrganizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery());

        Assert.Equal(1, result.Page);
        Assert.Equal(RepairOrderListQuery.DefaultPageSize, result.PageSize);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(20, result.Items.Count);
    }

    [Fact]
    public async Task RepairOrderList_PageTwo_ReturnsNextRows()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 25);

        var service = CreateService(scope.Context, scope.OrganizationContext);
        var pageOne = await service.ListRepairOrdersAsync(new RepairOrderListQuery { Page = 1, PageSize = 10 });
        var pageTwo = await service.ListRepairOrdersAsync(new RepairOrderListQuery { Page = 2, PageSize = 10 });

        Assert.Equal(10, pageOne.Items.Count);
        Assert.Equal(10, pageTwo.Items.Count);
        Assert.Equal(25, pageTwo.TotalCount);
        Assert.Empty(pageOne.Items.Select(item => item.RepairOrderId)
            .Intersect(pageTwo.Items.Select(item => item.RepairOrderId)));
    }

    [Fact]
    public async Task RepairOrderList_MaxPageSize_IsCappedAtOneHundred()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 3);

        var service = CreateService(scope.Context, scope.OrganizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery { PageSize = 500 });

        Assert.Equal(RepairOrderListQuery.MaxPageSize, result.PageSize);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task RepairOrderList_InvalidPage_NormalizesToFirstPage()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 3);

        var service = CreateService(scope.Context, scope.OrganizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery { Page = 0 });

        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task RepairOrderList_PageBeyondFinalPage_ReturnsEmptyItemsWithoutException()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 5);

        var service = CreateService(scope.Context, scope.OrganizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery { Page = 99, PageSize = 10 });

        Assert.Equal(99, result.Page);
        Assert.Equal(5, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task RepairOrderList_FilteredSearch_ReturnsFilteredTotalCount()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 5);

        var service = CreateService(scope.Context, scope.OrganizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery { Search = $"{suffix}-target" });

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Contains($"{suffix}-target", result.Items[0].Number, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepairOrderList_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);

        await TestDataFactory.PersistRepairOrderAsync(
            writeScope, organization.Id, location.Id, customer.Id, vehicle.Id,
            $"{suffix}-draft", BaseOpenedAt, RepairOrderStatus.Draft);
        await TestDataFactory.PersistRepairOrderAsync(
            writeScope, organization.Id, location.Id, customer.Id, vehicle.Id,
            $"{suffix}-inprogress", BaseOpenedAt.AddHours(1), RepairOrderStatus.InProgress);

        var service = CreateService(writeScope, organizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery
        {
            Status = RepairOrderStatus.InProgress,
        });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(RepairOrderStatus.InProgress, result.Items[0].Status);
    }

    [Fact]
    public async Task RepairOrderList_Ordering_IsStableByOpenedAtDescThenIdAsc()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);

        var sharedOpenedAt = BaseOpenedAt;
        var older = await TestDataFactory.PersistRepairOrderAsync(
            writeScope, organization.Id, location.Id, customer.Id, vehicle.Id,
            $"{suffix}-older", sharedOpenedAt);
        var newer = await TestDataFactory.PersistRepairOrderAsync(
            writeScope, organization.Id, location.Id, customer.Id, vehicle.Id,
            $"{suffix}-newer", sharedOpenedAt.AddHours(2));
        var middle = await TestDataFactory.PersistRepairOrderAsync(
            writeScope, organization.Id, location.Id, customer.Id, vehicle.Id,
            $"{suffix}-middle", sharedOpenedAt.AddHours(1));

        var service = CreateService(writeScope, organizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery { PageSize = 10 });

        Assert.Equal(
            [newer.Id, middle.Id, older.Id],
            result.Items.Select(item => item.RepairOrderId).ToArray());
    }

    [Fact]
    public async Task RepairOrderList_TenantIsolation_ExcludesOtherOrganizationRowsFromCountAndPage()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(scopeA, organizationA.Id, suffix);
            for (var index = 0; index < 3; index++)
            {
                await TestDataFactory.PersistRepairOrderAsync(
                    scopeA, organizationA.Id, location.Id, customer.Id, vehicle.Id,
                    $"{suffix}-a-{index}", BaseOpenedAt.AddHours(index));
            }
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(scopeB, organizationB.Id, suffix);
            await TestDataFactory.PersistRepairOrderAsync(
                scopeB, organizationB.Id, location.Id, customer.Id, vehicle.Id,
                $"{suffix}-b", BaseOpenedAt);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Contains($"{suffix}-a-", item.Number, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepairOrderList_EmptyResult_ReturnsZeroTotalCount()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext);

        var service = CreateService(writeScope, organizationContext);
        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery());

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task RepairOrderList_UsesCountAndPagedQueriesOnly()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await CreatePopulatedScopeAsync(suffix, repairOrderCount: 25);

        var counter = new SqlCommandCounter();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scope.Context.Database.GetDbConnection())
            .AddInterceptors(counter)
            .Options;

        await using var instrumentedContext = new AppDbContext(
            options,
            scope.OrganizationContext,
            TimeProvider.System);
        await instrumentedContext.Database.UseTransactionAsync(
            scope.Context.Database.CurrentTransaction!.GetDbTransaction());

        var service = new RepairOrderManagementService(
            instrumentedContext,
            scope.OrganizationContext,
            new RepairOrderNumberGenerator(),
            TimeProvider.System);

        var result = await service.ListRepairOrdersAsync(new RepairOrderListQuery { Page = 1, PageSize = 10 });

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(2, counter.CommandCount);
    }

    private async Task<PaginationTestScope> CreatePopulatedScopeAsync(string suffix, int repairOrderCount)
    {
        var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);
        var writeScope = scope.CreateContext(organizationContext);
        var (location, customer, vehicle) = await PersistIntakePrerequisitesAsync(writeScope, organization.Id, suffix);

        for (var index = 0; index < repairOrderCount; index++)
        {
            var label = index == 0 ? $"{suffix}-target" : $"{suffix}-{index:D2}";
            await TestDataFactory.PersistRepairOrderAsync(
                writeScope,
                organization.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                label,
                BaseOpenedAt.AddHours(index));
        }

        return new PaginationTestScope(scope, writeScope, organizationContext);
    }

    private static RepairOrderManagementService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext) =>
        new(
            context,
            organizationContext,
            new RepairOrderNumberGenerator(),
            TimeProvider.System);

    private static async Task<(WorkshopOS.Domain.Organizations.WorkshopLocation Location, WorkshopOS.Domain.Customers.Customer Customer, WorkshopOS.Domain.Vehicles.Vehicle Vehicle)> PersistIntakePrerequisitesAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(context, organizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(context, organizationId, suffix, customer.Id);
        return (location, customer, vehicle);
    }

    private sealed class PaginationTestScope : IAsyncDisposable
    {
        private readonly DatabaseTransactionScope _fixtureScope;
        private readonly AppDbContext _context;

        public PaginationTestScope(
            DatabaseTransactionScope fixtureScope,
            AppDbContext context,
            IOrganizationContext organizationContext)
        {
            _fixtureScope = fixtureScope;
            _context = context;
            OrganizationContext = organizationContext;
        }

        public AppDbContext Context => _context;

        public IOrganizationContext OrganizationContext { get; }

        public ValueTask DisposeAsync() => _fixtureScope.DisposeAsync();
    }

    private sealed class SqlCommandCounter : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandCount++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
