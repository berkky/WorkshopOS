using WorkshopOS.Application.Reporting;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Reporting;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class ReportingTestSupport
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    public static IWorkshopReportingService CreateReportingService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null) =>
        new WorkshopReportingService(
            context,
            organizationContext,
            timeProvider ?? new FakeTimeProvider(DefaultNow));

    public static async Task<ReportingScenario> CreateReportingScenarioAsync(
        DatabaseTransactionScope scope,
        string suffix,
        OrganizationMembershipRole role = OrganizationMembershipRole.Owner)
    {
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var actor = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, actor.Id, role);

        await using var writeScope = scope.CreateContext(
            new TestOrganizationContext(organization.Id),
            new FakeTimeProvider(DefaultNow));

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

        return new ReportingScenario(organization.Id, actor.Id, location.Id, repairOrder.Id);
    }

    public static ReportingQuery CreateQuery(Guid actorUserId, Guid? workshopLocationId = null) =>
        new()
        {
            ActorUserId = actorUserId,
            WorkshopLocationId = workshopLocationId,
        };

    public sealed record ReportingScenario(
        Guid OrganizationId,
        Guid ActorUserId,
        Guid WorkshopLocationId,
        Guid RepairOrderId);
}
