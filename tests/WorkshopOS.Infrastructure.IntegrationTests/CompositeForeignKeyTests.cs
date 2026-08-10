using Microsoft.EntityFrameworkCore;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Inventory;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CompositeForeignKeyTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantVehicleCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix);
        scopeA.Entry(vehicleA).Property<Guid?>(nameof(Vehicle.CurrentCustomerId)).CurrentValue = customerB.Id;

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantAppointmentCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var start = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix);

        scopeA.Appointments.Add(new Appointment(
            organizationA.Id,
            locationA.Id,
            customerB.Id,
            vehicleA.Id,
            start,
            end));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantAppointmentLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var start = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);

        scopeA.Appointments.Add(new Appointment(
            organizationA.Id,
            locationB.Id,
            customerA.Id,
            vehicleA.Id,
            start,
            end));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantAppointmentVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var start = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);

        scopeA.Appointments.Add(new Appointment(
            organizationA.Id,
            locationA.Id,
            customerA.Id,
            vehicleB.Id,
            start,
            end));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantRepairOrderCustomer()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix);

        scopeA.RepairOrders.Add(new RepairOrder(
            organizationA.Id,
            locationA.Id,
            customerB.Id,
            vehicleA.Id,
            $"RO-{suffix}",
            openedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantRepairOrderVehicle()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);

        scopeA.RepairOrders.Add(new RepairOrder(
            organizationA.Id,
            locationA.Id,
            customerA.Id,
            vehicleB.Id,
            $"RO-{suffix}",
            openedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantRepairOrderLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);

        scopeA.RepairOrders.Add(new RepairOrder(
            organizationA.Id,
            locationB.Id,
            customerA.Id,
            vehicleA.Id,
            $"RO-{suffix}",
            openedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsCrossTenantRepairOrderAppointment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
        var appointmentB = await TestDataFactory.PersistAppointmentAsync(
            scopeB,
            organizationB.Id,
            locationB.Id,
            customerB.Id,
            vehicleB.Id,
            start,
            end);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);

        scopeA.RepairOrders.Add(new RepairOrder(
            organizationA.Id,
            locationA.Id,
            customerA.Id,
            vehicleA.Id,
            $"RO-{suffix}",
            openedAt,
            appointmentId: appointmentB.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task Estimate_CrossTenantRepairOrder_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
        var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
            scopeB,
            organizationB.Id,
            locationB.Id,
            customerB.Id,
            vehicleB.Id,
            suffix,
            openedAt);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.Estimates.Add(new Estimate(organizationA.Id, repairOrderB.Id, $"EST-{suffix}", "TRY"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task EstimateItem_CrossTenantEstimate_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var estimateB = new Estimate(organizationB.Id, repairOrderB.Id, $"EST-{suffix}", "TRY");
            scopeB.Estimates.Add(estimateB);
            await scopeB.SaveChangesAsync();
            estimateBId = estimateB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.EstimateItems.Add(new EstimateItem(
            organizationA.Id,
            estimateBId,
            EstimateItemType.Service,
            "Cross-tenant item",
            1,
            10m,
            10));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task Inspection_CrossTenantRepairOrder_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
        var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
            scopeB,
            organizationB.Id,
            locationB.Id,
            customerB.Id,
            vehicleB.Id,
            suffix,
            openedAt);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.Inspections.Add(new Inspection(organizationA.Id, repairOrderB.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InspectionItem_CrossTenantInspection_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid inspectionBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var inspectionB = new Inspection(organizationB.Id, repairOrderB.Id);
            scopeB.Inspections.Add(inspectionB);
            await scopeB.SaveChangesAsync();
            inspectionBId = inspectionB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.InspectionItems.Add(new InspectionItem(
            organizationA.Id,
            inspectionBId,
            "Section",
            "Item",
            InspectionCondition.NotChecked,
            10));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InspectionMedia_CrossTenantInspection_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var uploadedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var uploaderA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            uploaderA.Id,
            OrganizationMembershipRole.Owner);

        Guid inspectionBId;
        Guid itemBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var inspectionB = new Inspection(organizationB.Id, repairOrderB.Id);
            scopeB.Inspections.Add(inspectionB);
            await scopeB.SaveChangesAsync();
            var itemB = new InspectionItem(organizationB.Id, inspectionB.Id, "Section", "Item", InspectionCondition.NotChecked, 10);
            scopeB.InspectionItems.Add(itemB);
            await scopeB.SaveChangesAsync();
            inspectionBId = inspectionB.Id;
            itemBId = itemB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.InspectionMediaAssets.Add(CreateTestMediaAsset(
            organizationA.Id,
            inspectionBId,
            itemBId,
            uploaderA.Id,
            uploadedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InspectionMedia_CrossTenantInspectionItem_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var uploadedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var uploaderA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            uploaderA.Id,
            OrganizationMembershipRole.Owner);

        Guid itemBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var inspectionB = new Inspection(organizationB.Id, repairOrderB.Id);
            scopeB.Inspections.Add(inspectionB);
            await scopeB.SaveChangesAsync();
            var itemB = new InspectionItem(organizationB.Id, inspectionB.Id, "Section", "Item", InspectionCondition.NotChecked, 10);
            scopeB.InspectionItems.Add(itemB);
            await scopeB.SaveChangesAsync();
            itemBId = itemB.Id;
        }

        Guid inspectionAId;
        Guid itemAId;
        await using (var scopeASetup = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeASetup, organizationA.Id, suffix);
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeASetup, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeASetup, organizationA.Id, suffix, customerA.Id);
            var repairOrderA = await TestDataFactory.PersistRepairOrderAsync(
                scopeASetup,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                suffix,
                openedAt);
            var inspectionA = new Inspection(organizationA.Id, repairOrderA.Id);
            scopeASetup.Inspections.Add(inspectionA);
            await scopeASetup.SaveChangesAsync();
            var itemA = new InspectionItem(organizationA.Id, inspectionA.Id, "Section", "Item", InspectionCondition.NotChecked, 10);
            scopeASetup.InspectionItems.Add(itemA);
            await scopeASetup.SaveChangesAsync();
            inspectionAId = inspectionA.Id;
            itemAId = itemA.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.InspectionMediaAssets.Add(CreateTestMediaAsset(
            organizationA.Id,
            inspectionAId,
            itemBId,
            uploaderA.Id,
            uploadedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InspectionMedia_ItemFromDifferentInspection_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var uploadedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var uploader = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            uploader.Id,
            OrganizationMembershipRole.Owner);

        Guid inspectionAId;
        Guid itemBId;
        await using (var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id)))
        {
            var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
            var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
            var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);
            var repairOrderA = await TestDataFactory.PersistRepairOrderAsync(
                writeScope,
                organization.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                $"{suffix}-a",
                openedAt);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                writeScope,
                organization.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                $"{suffix}-b",
                openedAt);
            var inspectionA = new Inspection(organization.Id, repairOrderA.Id);
            var inspectionB = new Inspection(organization.Id, repairOrderB.Id);
            writeScope.Inspections.AddRange(inspectionA, inspectionB);
            await writeScope.SaveChangesAsync();
            var itemB = new InspectionItem(organization.Id, inspectionB.Id, "Section", "Item", InspectionCondition.NotChecked, 10);
            writeScope.InspectionItems.Add(itemB);
            await writeScope.SaveChangesAsync();
            inspectionAId = inspectionA.Id;
            itemBId = itemB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organization.Id));
        scopeA.InspectionMediaAssets.Add(CreateTestMediaAsset(
            organization.Id,
            inspectionAId,
            itemBId,
            uploader.Id,
            uploadedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InspectionMedia_CrossTenantUploaderMembership_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var uploadedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var uploaderB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            uploaderB.Id,
            OrganizationMembershipRole.Owner);

        Guid inspectionAId;
        Guid itemAId;
        await using (var scopeASetup = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeASetup, organizationA.Id, suffix);
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeASetup, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeASetup, organizationA.Id, suffix, customerA.Id);
            var repairOrderA = await TestDataFactory.PersistRepairOrderAsync(
                scopeASetup,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                suffix,
                openedAt);
            var inspectionA = new Inspection(organizationA.Id, repairOrderA.Id);
            scopeASetup.Inspections.Add(inspectionA);
            await scopeASetup.SaveChangesAsync();
            var itemA = new InspectionItem(organizationA.Id, inspectionA.Id, "Section", "Item", InspectionCondition.NotChecked, 10);
            scopeASetup.InspectionItems.Add(itemA);
            await scopeASetup.SaveChangesAsync();
            inspectionAId = inspectionA.Id;
            itemAId = itemA.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.InspectionMediaAssets.Add(CreateTestMediaAsset(
            organizationA.Id,
            inspectionAId,
            itemAId,
            uploaderB.Id,
            uploadedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    private static InspectionMediaAsset CreateTestMediaAsset(
        Guid organizationId,
        Guid inspectionId,
        Guid inspectionItemId,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAtUtc) =>
        new(
            organizationId,
            inspectionId,
            inspectionItemId,
            $"{Guid.CreateVersion7():N}.jpg",
            InspectionMediaKind.Photo,
            "image/jpeg",
            16,
            new string('a', 64),
            uploadedByUserId,
            uploadedAtUtc);

    [Fact]
    public async Task EstimateShare_CrossTenantEstimate_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = openedAt.AddDays(7);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var creatorA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            creatorA.Id,
            OrganizationMembershipRole.Owner);

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var estimateB = new Estimate(organizationB.Id, repairOrderB.Id, $"EST-{suffix}", "TRY");
            scopeB.Estimates.Add(estimateB);
            await scopeB.SaveChangesAsync();
            estimateBId = estimateB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.EstimateShares.Add(new EstimateShare(
            organizationA.Id,
            Guid.CreateVersion7(),
            estimateBId,
            new string('a', 64),
            expiresAt,
            creatorA.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task EstimateShare_CrossTenantCreatorMembership_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = openedAt.AddDays(7);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var creatorB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            creatorB.Id,
            OrganizationMembershipRole.Owner);

        Guid estimateAId;
        await using (var scopeASetup = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeASetup, organizationA.Id, suffix);
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeASetup, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeASetup, organizationA.Id, suffix, customerA.Id);
            var repairOrderA = await TestDataFactory.PersistRepairOrderAsync(
                scopeASetup,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                suffix,
                openedAt);
            var estimateA = new Estimate(organizationA.Id, repairOrderA.Id, $"EST-{suffix}", "TRY");
            scopeASetup.Estimates.Add(estimateA);
            await scopeASetup.SaveChangesAsync();
            estimateAId = estimateA.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.EstimateShares.Add(new EstimateShare(
            organizationA.Id,
            Guid.CreateVersion7(),
            estimateAId,
            new string('b', 64),
            expiresAt,
            creatorB.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task EstimateShare_SecondCurrentShareForSameEstimate_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = openedAt.AddDays(7);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var creator = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            creator.Id,
            OrganizationMembershipRole.Owner);

        Guid estimateId;
        await using (var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id)))
        {
            var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
            var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
            var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);
            var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
                writeScope,
                organization.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                suffix,
                openedAt);
            var estimate = new Estimate(organization.Id, repairOrder.Id, $"EST-{suffix}", "TRY");
            writeScope.Estimates.Add(estimate);
            await writeScope.SaveChangesAsync();
            estimateId = estimate.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organization.Id));
        scopeA.EstimateShares.Add(new EstimateShare(
            organization.Id,
            Guid.CreateVersion7(),
            estimateId,
            new string('c', 64),
            expiresAt,
            creator.Id));
        await scopeA.SaveChangesAsync();

        scopeA.EstimateShares.Add(new EstimateShare(
            organization.Id,
            Guid.CreateVersion7(),
            estimateId,
            new string('d', 64),
            expiresAt.AddDays(1),
            creator.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task PartInventoryBalance_CrossTenantPart_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid partBId;
        Guid locationAId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var partB = new PartCatalogItem(organizationB.Id, $"PART-{suffix}", "Part B", null, 10m, "TRY");
            scopeB.PartCatalogItems.Add(partB);
            await scopeB.SaveChangesAsync();
            partBId = partB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        locationAId = (await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix)).Id;
        scopeA.PartInventoryBalances.Add(new PartInventoryBalance(organizationA.Id, partBId, locationAId, 0m));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task PartInventoryBalance_CrossTenantLocation_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid locationBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            locationBId = (await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix)).Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var partA = new PartCatalogItem(organizationA.Id, $"PART-{suffix}", "Part A", null, 10m, "TRY");
        scopeA.PartCatalogItems.Add(partA);
        await scopeA.SaveChangesAsync();
        scopeA.PartInventoryBalances.Add(new PartInventoryBalance(organizationA.Id, partA.Id, locationBId, 0m));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task PartInventoryMovement_CrossTenantBalance_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var occurredAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var actorA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            actorA.Id,
            OrganizationMembershipRole.Owner);

        Guid partBId;
        Guid locationBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var partB = new PartCatalogItem(organizationB.Id, $"PART-{suffix}", "Part B", null, 10m, "TRY");
            scopeB.PartCatalogItems.Add(partB);
            locationBId = (await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix)).Id;
            await scopeB.SaveChangesAsync();
            partBId = partB.Id;
            scopeB.PartInventoryBalances.Add(new PartInventoryBalance(organizationB.Id, partBId, locationBId, 5m));
            await scopeB.SaveChangesAsync();
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.PartInventoryMovements.Add(new PartInventoryMovement(
            organizationA.Id,
            partBId,
            locationBId,
            PartInventoryMovementType.ManualIncrease,
            1m,
            1m,
            actorA.Id,
            occurredAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task PartInventoryMovement_CrossTenantRecordedByMembership_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var occurredAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var actorB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            actorB.Id,
            OrganizationMembershipRole.Owner);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var partA = new PartCatalogItem(organizationA.Id, $"PART-{suffix}", "Part A", null, 10m, "TRY");
        scopeA.PartCatalogItems.Add(partA);
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        await scopeA.SaveChangesAsync();
        scopeA.PartInventoryBalances.Add(new PartInventoryBalance(organizationA.Id, partA.Id, locationA.Id, 0m));
        await scopeA.SaveChangesAsync();

        scopeA.PartInventoryMovements.Add(new PartInventoryMovement(
            organizationA.Id,
            partA.Id,
            locationA.Id,
            PartInventoryMovementType.OpeningBalance,
            1m,
            1m,
            actorB.Id,
            occurredAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task Inventory_SecondBalanceForSamePartAndLocation_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var part = new PartCatalogItem(organization.Id, $"PART-{suffix}", "Part", null, 10m, "TRY");
        writeScope.PartCatalogItems.Add(part);
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        await writeScope.SaveChangesAsync();

        writeScope.PartInventoryBalances.Add(new PartInventoryBalance(organization.Id, part.Id, location.Id, 1m));
        await writeScope.SaveChangesAsync();

        writeScope.ChangeTracker.Clear();
        writeScope.PartInventoryBalances.Add(new PartInventoryBalance(organization.Id, part.Id, location.Id, 2m));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writeScope.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task ServiceCatalog_DuplicateNormalizedCodeInOrganization_IsRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        writeScope.ServiceCatalogItems.Add(new ServiceCatalogItem(
            organization.Id,
            "svc-alpha",
            "Service A",
            null,
            10m,
            "TRY"));
        await writeScope.SaveChangesAsync();

        writeScope.ServiceCatalogItems.Add(new ServiceCatalogItem(
            organization.Id,
            "  SVC-ALPHA  ",
            "Service B",
            null,
            20m,
            "TRY"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writeScope.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task PartCatalog_DuplicateNormalizedSkuInOrganization_IsRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        writeScope.PartCatalogItems.Add(new PartCatalogItem(
            organization.Id,
            "part-alpha",
            "Part A",
            null,
            10m,
            "TRY"));
        await writeScope.SaveChangesAsync();

        writeScope.PartCatalogItems.Add(new PartCatalogItem(
            organization.Id,
            "  PART-ALPHA  ",
            "Part B",
            null,
            20m,
            "TRY"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writeScope.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task ServiceCatalog_SameNormalizedCodeAllowedAcrossOrganizations()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            scopeA.ServiceCatalogItems.Add(new ServiceCatalogItem(
                organizationA.Id,
                "SVC-SHARED",
                "Service A",
                null,
                10m,
                "TRY"));
            await scopeA.SaveChangesAsync();
        }

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        scopeB.ServiceCatalogItems.Add(new ServiceCatalogItem(
            organizationB.Id,
            "svc-shared",
            "Service B",
            null,
            15m,
            "TRY"));
        await scopeB.SaveChangesAsync();

        Assert.Equal(1, await scopeB.ServiceCatalogItems.CountAsync());
    }

    [Fact]
    public async Task PartCatalog_SameNormalizedSkuAllowedAcrossOrganizations()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            scopeA.PartCatalogItems.Add(new PartCatalogItem(
                organizationA.Id,
                "PART-SHARED",
                "Part A",
                null,
                10m,
                "TRY"));
            await scopeA.SaveChangesAsync();
        }

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        scopeB.PartCatalogItems.Add(new PartCatalogItem(
            organizationB.Id,
            "part-shared",
            "Part B",
            null,
            15m,
            "TRY"));
        await scopeB.SaveChangesAsync();

        Assert.Equal(1, await scopeB.PartCatalogItems.CountAsync());
    }

    [Fact]
    public async Task Invoice_CrossTenantRepairOrder_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        await using var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id));
        var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
        var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
        var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
        var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
            scopeB,
            organizationB.Id,
            locationB.Id,
            customerB.Id,
            vehicleB.Id,
            suffix,
            openedAt);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.Invoices.Add(new Invoice(organizationA.Id, repairOrderB.Id, $"INV-{suffix}", "TRY"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InvoiceItem_CrossTenantInvoice_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid invoiceBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var invoiceB = new Invoice(organizationB.Id, repairOrderB.Id, $"INV-{suffix}", "TRY");
            scopeB.Invoices.Add(invoiceB);
            await scopeB.SaveChangesAsync();
            invoiceBId = invoiceB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.InvoiceItems.Add(new InvoiceItem(
            organizationA.Id,
            invoiceBId,
            InvoiceItemType.Service,
            "Cross-tenant item",
            1,
            10m,
            10));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InvoicePaymentRecord_CrossTenantInvoice_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var recordedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var actorA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            actorA.Id,
            OrganizationMembershipRole.Owner);

        Guid invoiceBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            var repairOrderB = await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                openedAt);
            var invoiceB = new Invoice(organizationB.Id, repairOrderB.Id, $"INV-{suffix}", "TRY");
            scopeB.Invoices.Add(invoiceB);
            await scopeB.SaveChangesAsync();
            invoiceBId = invoiceB.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        scopeA.InvoicePaymentRecords.Add(new InvoicePaymentRecord(
            organizationA.Id,
            invoiceBId,
            10m,
            PaymentMethod.Cash,
            actorA.Id,
            recordedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task InvoicePaymentRecord_CrossTenantRecordedByMembership_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var recordedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var actorB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationB.Id,
            actorB.Id,
            OrganizationMembershipRole.Owner);

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
        var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);
        var repairOrderA = await TestDataFactory.PersistRepairOrderAsync(
            scopeA,
            organizationA.Id,
            locationA.Id,
            customerA.Id,
            vehicleA.Id,
            suffix,
            openedAt);
        var invoiceA = new Invoice(organizationA.Id, repairOrderA.Id, $"INV-{suffix}", "TRY");
        scopeA.Invoices.Add(invoiceA);
        await scopeA.SaveChangesAsync();

        scopeA.InvoicePaymentRecords.Add(new InvoicePaymentRecord(
            organizationA.Id,
            invoiceA.Id,
            10m,
            PaymentMethod.Cash,
            actorB.Id,
            recordedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task Invoice_SecondActiveInvoiceForRepairOrder_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id));
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            suffix,
            openedAt);

        writeScope.Invoices.Add(new Invoice(organization.Id, repairOrder.Id, $"INV-{suffix}-1", "TRY"));
        await writeScope.SaveChangesAsync();

        writeScope.ChangeTracker.Clear();
        writeScope.Invoices.Add(new Invoice(organization.Id, repairOrder.Id, $"INV-{suffix}-2", "TRY"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writeScope.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task Invoice_SecondActiveInvoiceForSourceEstimate_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var openedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);

        Guid estimateId;
        Guid repairOrderId;
        await using (var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id)))
        {
            var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
            var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
            var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);
            var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
                writeScope,
                organization.Id,
                location.Id,
                customer.Id,
                vehicle.Id,
                suffix,
                openedAt);
            repairOrderId = repairOrder.Id;
            var estimate = new Estimate(organization.Id, repairOrder.Id, $"EST-{suffix}", "TRY");
            writeScope.Estimates.Add(estimate);
            await writeScope.SaveChangesAsync();
            estimateId = estimate.Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organization.Id));
        scopeA.Invoices.Add(new Invoice(organization.Id, repairOrderId, $"INV-{suffix}-1", "TRY", estimateId));
        await scopeA.SaveChangesAsync();

        scopeA.ChangeTracker.Clear();
        scopeA.Invoices.Add(new Invoice(organization.Id, repairOrderId, $"INV-{suffix}-2", "TRY", estimateId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }
}
