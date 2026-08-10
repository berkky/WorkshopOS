using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Operations;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Operations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepairOrderTechnicianAssignmentTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RepairOrderTechnicianAssignment_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(
            context,
            typeof(RepairOrderTechnicianAssignment)));
    }

    [Fact]
    public async Task RepairOrderTechnicianAssignment_CrossTenantRepairOrder_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            repairOrderBId = (await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                suffix,
                DefaultNow)).Id;
        }

        await using var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            scopeA,
            organizationA.Id,
            locationA.Id,
            suffix);

        scopeA.RepairOrderTechnicianAssignments.Add(new RepairOrderTechnicianAssignment(
            organizationA.Id,
            repairOrderBId,
            technician.StaffMember.Id,
            DefaultNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task RepairOrderTechnicianAssignment_CrossTenantStaffMember_IsRejectedByPostgreSql()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid staffMemberBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            staffMemberBId = (await TestDataFactory.PersistTechnicianAtLocationAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                suffix)).StaffMember.Id;
        }

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
            DefaultNow);

        scopeA.RepairOrderTechnicianAssignments.Add(new RepairOrderTechnicianAssignment(
            organizationA.Id,
            repairOrderA.Id,
            staffMemberBId,
            DefaultNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scopeA.SaveChangesAsync());
        Assert.Equal("23503", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task RepairOrderTechnicianAssignment_RejectsDuplicateActiveAssignment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

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
            DefaultNow);
        var firstTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            location.Id,
            $"{suffix}-first");
        var secondTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            location.Id,
            $"{suffix}-second");

        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            organization.Id,
            repairOrder.Id,
            firstTechnician.StaffMember.Id,
            DefaultNow);

        writeScope.RepairOrderTechnicianAssignments.Add(new RepairOrderTechnicianAssignment(
            organization.Id,
            repairOrder.Id,
            secondTechnician.StaffMember.Id,
            DefaultNow.AddMinutes(5)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writeScope.SaveChangesAsync());
        Assert.Equal("23505", exception.GetPostgresSqlState());
    }

    [Fact]
    public async Task RepairOrderTechnicianAssignment_AllowsHistoricalReassignment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var reassignedAt = DefaultNow.AddHours(2);

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
            DefaultNow);
        var firstTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            location.Id,
            $"{suffix}-first");
        var secondTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            location.Id,
            $"{suffix}-second");

        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            organization.Id,
            repairOrder.Id,
            firstTechnician.StaffMember.Id,
            DefaultNow,
            reassignedAt);

        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            organization.Id,
            repairOrder.Id,
            secondTechnician.StaffMember.Id,
            reassignedAt);

        var assignments = await writeScope.RepairOrderTechnicianAssignments
            .Where(candidate => candidate.RepairOrderId == repairOrder.Id)
            .OrderBy(candidate => candidate.AssignedAtUtc)
            .ToListAsync();

        Assert.Equal(2, assignments.Count);
        Assert.NotNull(assignments[0].UnassignedAtUtc);
        Assert.Null(assignments[1].UnassignedAtUtc);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class WorkshopOperationsServiceTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            });

        Assert.True(result.Success);
        var assignment = await writeScope.RepairOrderTechnicianAssignments.SingleAsync();
        Assert.Equal(organization.Id, assignment.OrganizationId);
        Assert.Equal(scenario.RepairOrder.Id, assignment.RepairOrderId);
        Assert.Equal(scenario.Technician.StaffMember.Id, assignment.StaffMemberId);
    }

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_RejectsOtherTenantStaff()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organizationA.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        Guid technicianBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            technicianBId = (await TestDataFactory.PersistTechnicianAtLocationAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                suffix)).StaffMember.Id;
        }

        var organizationContext = new TestOrganizationContext(organizationA.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organizationA.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = technicianBId,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.StaffMemberNotFound, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_RejectsInactiveStaff()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        scenario.Technician.StaffMember.Deactivate();
        await writeScope.SaveChangesAsync();
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.StaffInactive, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_RejectsNonTechnicianPosition()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var advisor = await TestDataFactory.PersistStaffMemberAsync(
            writeScope,
            organization.Id,
            $"{suffix}-advisor",
            position: StaffPosition.ServiceAdvisor);
        await TestDataFactory.PersistStaffLocationAssignmentAsync(
            writeScope,
            organization.Id,
            advisor.Id,
            scenario.Location.Id);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = advisor.Id,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.TechnicianNotEligible, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_RejectsTechnicianWithoutRepairOrderLocation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var otherLocation = await TestDataFactory.PersistWorkshopLocationAsync(
            writeScope,
            organization.Id,
            $"{suffix}-other");
        var remoteTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            otherLocation.Id,
            $"{suffix}-remote");
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = remoteTechnician.StaffMember.Id,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.LocationMismatch, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_AllowsEligibleTechnician()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            });

        Assert.True(result.Success);
        Assert.Equal(TechnicianWorkStatus.Assigned, (await writeScope.RepairOrderTechnicianAssignments.SingleAsync()).WorkStatus);
    }

    [Fact]
    public async Task WorkshopOperations_AssignTechnician_RejectsTerminalRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        scenario.RepairOrder.Cancel();
        await writeScope.SaveChangesAsync();
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.TerminalRepairOrder, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_ReassignTechnician_ClosesPreviousAssignment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var reassignedAt = DefaultNow.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var replacementTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            scenario.Location.Id,
            $"{suffix}-replacement");
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            })).Success);

        timeProvider.Advance(TimeSpan.FromHours(1));

        var result = await service.ReassignTechnicianAsync(
            manager.Id,
            new ReassignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                NewStaffMemberId = replacementTechnician.StaffMember.Id,
            });

        Assert.True(result.Success);

        var previousAssignment = await writeScope.RepairOrderTechnicianAssignments
            .SingleAsync(candidate => candidate.StaffMemberId == scenario.Technician.StaffMember.Id);
        Assert.Equal(reassignedAt, previousAssignment.UnassignedAtUtc);
    }

    [Fact]
    public async Task WorkshopOperations_ReassignTechnician_PreservesPreviousAssignmentHistory()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var replacementTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            scenario.Location.Id,
            $"{suffix}-replacement");
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            })).Success);

        timeProvider.Advance(TimeSpan.FromHours(1));

        Assert.True((await service.ReassignTechnicianAsync(
            manager.Id,
            new ReassignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                NewStaffMemberId = replacementTechnician.StaffMember.Id,
            })).Success);

        Assert.Equal(2, await writeScope.RepairOrderTechnicianAssignments.CountAsync(
            candidate => candidate.RepairOrderId == scenario.RepairOrder.Id));
    }

    [Fact]
    public async Task WorkshopOperations_ReassignTechnician_CreatesNewActiveAssignment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var replacementTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            scenario.Location.Id,
            $"{suffix}-replacement");
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            })).Success);

        timeProvider.Advance(TimeSpan.FromHours(1));

        Assert.True((await service.ReassignTechnicianAsync(
            manager.Id,
            new ReassignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                NewStaffMemberId = replacementTechnician.StaffMember.Id,
            })).Success);

        var activeAssignment = await writeScope.RepairOrderTechnicianAssignments
            .SingleAsync(candidate => candidate.UnassignedAtUtc == null);
        Assert.Equal(replacementTechnician.StaffMember.Id, activeAssignment.StaffMemberId);
        Assert.Equal(TechnicianWorkStatus.Assigned, activeAssignment.WorkStatus);
    }

    [Fact]
    public async Task WorkshopOperations_UnassignTechnician_PreservesAssignmentRow()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            })).Success);

        timeProvider.Advance(TimeSpan.FromMinutes(30));

        var result = await service.UnassignTechnicianAsync(
            manager.Id,
            new UnassignTechnicianCommand { RepairOrderId = scenario.RepairOrder.Id });

        Assert.True(result.Success);
        Assert.Equal(1, await writeScope.RepairOrderTechnicianAssignments.CountAsync());
        var assignment = await writeScope.RepairOrderTechnicianAssignments.SingleAsync();
        Assert.NotNull(assignment.UnassignedAtUtc);
    }

    [Fact]
    public async Task WorkshopOperations_NewRepairOrder_DefaultPriorityIsNormal()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

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
            DefaultNow);

        Assert.Equal(RepairOrderPriority.Normal, repairOrder.Priority);
    }

    [Fact]
    public async Task WorkshopOperations_ManagerCanChangePriority()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.ChangeRepairOrderPriorityAsync(
            manager.Id,
            new ChangeRepairOrderPriorityCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                Priority = RepairOrderPriority.Urgent,
            });

        Assert.True(result.Success);
        Assert.Equal(
            RepairOrderPriority.Urgent,
            (await writeScope.RepairOrders.SingleAsync()).Priority);
    }

    [Fact]
    public async Task WorkshopOperations_TechnicianCannotChangePriority()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            technicianUser.Id,
            OrganizationMembershipRole.Technician);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.ChangeRepairOrderPriorityAsync(
            technicianUser.Id,
            new ChangeRepairOrderPriorityCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                Priority = RepairOrderPriority.High,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.Unauthorized, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_TerminalRepairOrderPriorityChangeRejected()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        scenario.RepairOrder.Cancel();
        await writeScope.SaveChangesAsync();
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.ChangeRepairOrderPriorityAsync(
            manager.Id,
            new ChangeRepairOrderPriorityCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                Priority = RepairOrderPriority.High,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.TerminalRepairOrder, result.FailureReason);
    }

    [Fact]
    public async Task OperationsBoard_OrdersByPriorityThenOpenedTime()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var early = DefaultNow.AddHours(-2);
        var middle = DefaultNow.AddHours(-1);
        var late = DefaultNow;

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);

        var lowEarly = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-low-early",
            early,
            priority: RepairOrderPriority.Low);
        var urgentLate = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-urgent-late",
            late,
            priority: RepairOrderPriority.Urgent);
        var urgentEarly = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-urgent-early",
            early,
            priority: RepairOrderPriority.Urgent);
        var normalMiddle = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-normal-middle",
            middle,
            priority: RepairOrderPriority.Normal);

        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));
        var board = await service.GetOperationsBoardAsync(new OperationsBoardQuery());

        Assert.Equal(
            [urgentEarly.Id, urgentLate.Id, normalMiddle.Id, lowEarly.Id],
            board.Items.Select(item => item.RepairOrderId).ToArray());
    }

    [Fact]
    public async Task OperationsBoard_ReturnsOnlyCurrentOrganizationRepairOrders()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid repairOrderBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            repairOrderBId = (await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                customerB.Id,
                vehicleB.Id,
                $"{suffix}-b",
                DefaultNow)).Id;
        }

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);
            await TestDataFactory.PersistRepairOrderAsync(
                scopeA,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                $"{suffix}-a",
                DefaultNow);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var board = await service.GetOperationsBoardAsync(new OperationsBoardQuery());

        Assert.Single(board.Items);
        Assert.DoesNotContain(board.Items, item => item.RepairOrderId == repairOrderBId);
    }

    [Fact]
    public async Task OperationsBoard_DoesNotExposeOtherTenantAssignments()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

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
                $"{suffix}-b",
                DefaultNow);
            var technicianB = await TestDataFactory.PersistTechnicianAtLocationAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                $"{suffix}-b");
            await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
                scopeB,
                organizationB.Id,
                repairOrderB.Id,
                technicianB.StaffMember.Id,
                DefaultNow);
        }

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);
            await TestDataFactory.PersistRepairOrderAsync(
                scopeA,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                $"{suffix}-a",
                DefaultNow);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));
        var board = await service.GetOperationsBoardAsync(new OperationsBoardQuery());

        Assert.Single(board.Items);
        Assert.Null(board.Items[0].AssignedStaffMemberId);
        Assert.Null(board.Items[0].AssignedTechnicianDisplayName);
    }

    [Fact]
    public async Task OperationsBoard_UnassignedFilterWorks()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var organizationContext = new TestOrganizationContext(organization.Id);

        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);

        var unassigned = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-unassigned",
            DefaultNow);
        var assigned = await TestDataFactory.PersistRepairOrderAsync(
            writeScope,
            organization.Id,
            location.Id,
            customer.Id,
            vehicle.Id,
            $"{suffix}-assigned",
            DefaultNow.AddMinutes(5));
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            location.Id,
            suffix);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            writeScope,
            organization.Id,
            assigned.Id,
            technician.StaffMember.Id,
            DefaultNow);

        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));
        var board = await service.GetOperationsBoardAsync(new OperationsBoardQuery { UnassignedOnly = true });

        Assert.Single(board.Items);
        Assert.Equal(unassigned.Id, board.Items[0].RepairOrderId);
    }

    [Fact]
    public async Task OperationsBoard_LocationFilterIsTenantScoped()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid locationA1Id;
        Guid locationA2Id;
        Guid locationBId;
        Guid repairOrderA1Id;
        Guid repairOrderA2Id;

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            locationA1Id = (await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, $"{suffix}-a1")).Id;
            locationA2Id = (await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, $"{suffix}-a2")).Id;
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);
            repairOrderA1Id = (await TestDataFactory.PersistRepairOrderAsync(
                scopeA,
                organizationA.Id,
                locationA1Id,
                customerA.Id,
                vehicleA.Id,
                $"{suffix}-a1",
                DefaultNow)).Id;
            repairOrderA2Id = (await TestDataFactory.PersistRepairOrderAsync(
                scopeA,
                organizationA.Id,
                locationA2Id,
                customerA.Id,
                vehicleA.Id,
                $"{suffix}-a2",
                DefaultNow.AddMinutes(5))).Id;
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            locationBId = (await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix)).Id;
            var customerB = await TestDataFactory.PersistCustomerAsync(scopeB, organizationB.Id, suffix);
            var vehicleB = await TestDataFactory.PersistVehicleAsync(scopeB, organizationB.Id, suffix, customerB.Id);
            await TestDataFactory.PersistRepairOrderAsync(
                scopeB,
                organizationB.Id,
                locationBId,
                customerB.Id,
                vehicleB.Id,
                $"{suffix}-b",
                DefaultNow);
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));

        var locationOneBoard = await service.GetOperationsBoardAsync(new OperationsBoardQuery
        {
            WorkshopLocationId = locationA1Id,
        });
        Assert.Single(locationOneBoard.Items);
        Assert.Equal(repairOrderA1Id, locationOneBoard.Items[0].RepairOrderId);

        var otherTenantLocationBoard = await service.GetOperationsBoardAsync(new OperationsBoardQuery
        {
            WorkshopLocationId = locationBId,
        });
        Assert.Empty(otherTenantLocationBoard.Items);

        var allBoard = await service.GetOperationsBoardAsync(new OperationsBoardQuery());
        Assert.Equal(2, allBoard.Items.Count);
        Assert.Contains(allBoard.Items, item => item.RepairOrderId == repairOrderA2Id);
    }

    [Fact]
    public async Task OperationsBoard_AssignedTechnicianFilterIsTenantScoped()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

        Guid technicianAId;
        Guid repairOrderAId;
        Guid technicianBId;

        await using (var scopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id)))
        {
            var locationA = await TestDataFactory.PersistWorkshopLocationAsync(scopeA, organizationA.Id, suffix);
            var customerA = await TestDataFactory.PersistCustomerAsync(scopeA, organizationA.Id, suffix);
            var vehicleA = await TestDataFactory.PersistVehicleAsync(scopeA, organizationA.Id, suffix, customerA.Id);
            repairOrderAId = (await TestDataFactory.PersistRepairOrderAsync(
                scopeA,
                organizationA.Id,
                locationA.Id,
                customerA.Id,
                vehicleA.Id,
                $"{suffix}-a",
                DefaultNow)).Id;
            var technicianA = await TestDataFactory.PersistTechnicianAtLocationAsync(
                scopeA,
                organizationA.Id,
                locationA.Id,
                $"{suffix}-a");
            technicianAId = technicianA.StaffMember.Id;
            await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
                scopeA,
                organizationA.Id,
                repairOrderAId,
                technicianAId,
                DefaultNow);
        }

        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id)))
        {
            var locationB = await TestDataFactory.PersistWorkshopLocationAsync(scopeB, organizationB.Id, suffix);
            technicianBId = (await TestDataFactory.PersistTechnicianAtLocationAsync(
                scopeB,
                organizationB.Id,
                locationB.Id,
                $"{suffix}-b")).StaffMember.Id;
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        var service = CreateService(readScopeA, new TestOrganizationContext(organizationA.Id));

        var ownTechnicianBoard = await service.GetOperationsBoardAsync(new OperationsBoardQuery
        {
            AssignedStaffMemberId = technicianAId,
        });
        Assert.Single(ownTechnicianBoard.Items);
        Assert.Equal(repairOrderAId, ownTechnicianBoard.Items[0].RepairOrderId);

        var otherTenantTechnicianBoard = await service.GetOperationsBoardAsync(new OperationsBoardQuery
        {
            AssignedStaffMemberId = technicianBId,
        });
        Assert.Empty(otherTenantTechnicianBoard.Items);
    }

    [Fact]
    public async Task TechnicianWork_LinkedAssignedTechnicianCanStartOwnWork()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task TechnicianWork_StartTransitionsAssignmentToInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        Assert.True((await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);

        var assignment = await writeScope.RepairOrderTechnicianAssignments.SingleAsync();
        Assert.Equal(TechnicianWorkStatus.InProgress, assignment.WorkStatus);
        Assert.Equal(DefaultNow, assignment.StartedAtUtc);
    }

    [Fact]
    public async Task TechnicianWork_StartMovesDraftRepairOrderToInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        Assert.True((await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);

        var repairOrder = await writeScope.RepairOrders.SingleAsync();
        Assert.Equal(RepairOrderStatus.InProgress, repairOrder.Status);
    }

    [Fact]
    public async Task TechnicianWork_CannotStartAnotherTechniciansAssignment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var assignedTechnicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-assigned");
        var otherTechnicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-other");

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            assignedTechnicianUser.Id);
        await TestDataFactory.PersistMembershipAsync(
            writeScope,
            organization.Id,
            otherTechnicianUser.Id,
            OrganizationMembershipRole.Technician);
        await TestDataFactory.PersistTechnicianAtLocationAsync(
            writeScope,
            organization.Id,
            scenario.Location.Id,
            $"{suffix}-other",
            otherTechnicianUser.Id);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.StartAssignedWorkAsync(otherTechnicianUser.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.Unauthorized, result.FailureReason);
    }

    [Fact]
    public async Task TechnicianWork_UnlinkedUserCannotMutateAssignment()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var unlinkedUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-unlinked");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            unlinkedUser.Id,
            OrganizationMembershipRole.Technician);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        Assert.True((await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            })).Success);

        var result = await service.StartAssignedWorkAsync(unlinkedUser.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.TechnicianProfileNotLinked, result.FailureReason);
    }

    [Fact]
    public async Task TechnicianWork_InactiveStaffFailsClosed()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        scenario.Technician.StaffMember.Deactivate();
        await writeScope.SaveChangesAsync();
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.StaffInactive, result.FailureReason);
    }

    [Fact]
    public async Task TechnicianWork_SuspendedMembershipFailsClosed()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id,
            OrganizationMembershipStatus.Suspended);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var result = await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.MembershipInactive, result.FailureReason);
    }

    [Fact]
    public async Task TechnicianWork_CanMarkOwnWorkCompleted()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var completedAt = DefaultNow.AddHours(1);

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);
        timeProvider.Advance(TimeSpan.FromHours(1));

        var result = await service.CompleteAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id);

        Assert.True(result.Success);
        var assignment = await writeScope.RepairOrderTechnicianAssignments.SingleAsync();
        Assert.Equal(TechnicianWorkStatus.WorkCompleted, assignment.WorkStatus);
        Assert.Equal(completedAt, assignment.WorkCompletedAtUtc);
    }

    [Fact]
    public async Task TechnicianWork_WorkCompletedDoesNotCompleteRepairOrder()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);
        timeProvider.Advance(TimeSpan.FromHours(1));
        Assert.True((await service.CompleteAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);

        var repairOrder = await writeScope.RepairOrders.SingleAsync();
        Assert.Equal(RepairOrderStatus.InProgress, repairOrder.Status);
        Assert.Null(repairOrder.CompletedAtUtc);
    }

    [Fact]
    public async Task TechnicianWork_CannotReturnWorkCompletedToInProgress()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-technician");

        var organizationContext = new TestOrganizationContext(organization.Id);
        var timeProvider = new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, timeProvider);
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            technicianUser.Id);
        var service = CreateService(writeScope, organizationContext, timeProvider);

        Assert.True((await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);
        timeProvider.Advance(TimeSpan.FromHours(1));
        Assert.True((await service.CompleteAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id)).Success);

        var result = await service.StartAssignedWorkAsync(technicianUser.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.InvalidWorkTransition, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_OwnerWithLinkedTechnicianProfile_CanPerformSelfWork()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var ownerTechnician = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner-tech");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            ownerTechnician.Id,
            OrganizationMembershipRole.Owner);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignedTechnicianScenarioAsync(
            writeScope,
            organization.Id,
            suffix,
            ownerTechnician.Id,
            membershipRole: OrganizationMembershipRole.Owner,
            createMembership: false);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var context = await service.GetRepairOrderOperationsContextAsync(
            scenario.RepairOrder.Id,
            ownerTechnician.Id);

        Assert.NotNull(context);
        Assert.True(context.CanStartOwnWork);

        var result = await service.StartAssignedWorkAsync(ownerTechnician.Id, scenario.RepairOrder.Id);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task WorkshopOperations_LinkedTechnicianWithoutMembership_IsDenied()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var userWithoutMembership = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-no-membership");
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            manager.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        Assert.True((await service.AssignTechnicianAsync(
            manager.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            })).Success);

        var result = await service.StartAssignedWorkAsync(userWithoutMembership.Id, scenario.RepairOrder.Id);

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.MembershipInactive, result.FailureReason);
    }

    [Fact]
    public async Task WorkshopOperations_ManagerRoleRegression_ServiceAdvisorDemotedToTechnician_IsDenied()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var advisor = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-advisor");
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            advisor.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        await using var writeScope = scope.CreateContext(organizationContext, new FakeTimeProvider(DefaultNow));
        var scenario = await CreateAssignableScenarioAsync(writeScope, organization.Id, suffix);
        var service = CreateService(writeScope, organizationContext, new FakeTimeProvider(DefaultNow));

        var membership = await writeScope.OrganizationMemberships
            .SingleAsync(candidate => candidate.UserId == advisor.Id);
        membership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var result = await service.AssignTechnicianAsync(
            advisor.Id,
            new AssignTechnicianCommand
            {
                RepairOrderId = scenario.RepairOrder.Id,
                StaffMemberId = scenario.Technician.StaffMember.Id,
            });

        Assert.False(result.Success);
        Assert.Equal(WorkshopOperationFailureReason.Unauthorized, result.FailureReason);
    }

    private static WorkshopOperationsService CreateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new(context, organizationContext, timeProvider ?? TimeProvider.System);

    private static async Task<AssignableScenario> CreateAssignableScenarioAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix)
    {
        var location = await TestDataFactory.PersistWorkshopLocationAsync(context, organizationId, suffix);
        var customer = await TestDataFactory.PersistCustomerAsync(context, organizationId, suffix);
        var vehicle = await TestDataFactory.PersistVehicleAsync(context, organizationId, suffix, customer.Id);
        var repairOrder = await TestDataFactory.PersistRepairOrderAsync(
            context,
            organizationId,
            location.Id,
            customer.Id,
            vehicle.Id,
            suffix,
            DefaultNow);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            context,
            organizationId,
            location.Id,
            suffix);

        return new AssignableScenario(location, repairOrder, technician);
    }

    private static async Task<AssignedTechnicianScenario> CreateAssignedTechnicianScenarioAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        Guid technicianUserId,
        OrganizationMembershipStatus membershipStatus = OrganizationMembershipStatus.Active,
        OrganizationMembershipRole membershipRole = OrganizationMembershipRole.Technician,
        bool createMembership = true)
    {
        var assignable = await CreateAssignableScenarioAsync(context, organizationId, suffix);

        if (createMembership)
        {
            await TestDataFactory.PersistMembershipAsync(
                context,
                organizationId,
                technicianUserId,
                membershipRole,
                membershipStatus);
        }

        var linkedTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            context,
            organizationId,
            assignable.Location.Id,
            $"{suffix}-linked",
            technicianUserId);

        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            context,
            organizationId,
            assignable.RepairOrder.Id,
            linkedTechnician.StaffMember.Id,
            DefaultNow);

        return new AssignedTechnicianScenario(
            assignable.Location,
            assignable.RepairOrder,
            linkedTechnician);
    }

    private sealed record AssignableScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder,
        (StaffMember StaffMember, StaffLocationAssignment LocationAssignment) Technician);

    private sealed record AssignedTechnicianScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder,
        (StaffMember StaffMember, StaffLocationAssignment LocationAssignment) Technician);
}
