using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.CustomerPortal;
using WorkshopOS.Infrastructure.Estimates;
using WorkshopOS.Infrastructure.EstimateSharing;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class EstimateShareTestSupport
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    public static IEstimateSharingService CreateSharingService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider,
        IEstimateShareTokenGenerator? tokenGenerator = null) =>
        new EstimateSharingService(
            context,
            organizationContext,
            tokenGenerator ?? new EstimateShareTokenGenerator(),
            timeProvider);

    public static ICustomerEstimatePortalService CreatePortalService(
        AppDbContext context,
        IOrganizationContextMutator organizationContextMutator,
        TimeProvider timeProvider) =>
        new CustomerEstimatePortalService(context, organizationContextMutator, timeProvider);

    public static IEstimateManagementService CreateEstimateService(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider) =>
        new EstimateManagementService(
            context,
            organizationContext,
            new EstimateNumberGenerator(),
            timeProvider);

    public static async Task<EligibleRepairOrderScenario> CreateEligibleRepairOrderAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        RepairOrderStatus status = RepairOrderStatus.Draft,
        RepairOrderPriority priority = RepairOrderPriority.Normal,
        string? internalNotes = null)
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
            DefaultNow,
            status,
            priority);

        if (!string.IsNullOrWhiteSpace(internalNotes))
        {
            context.Entry(repairOrder).Property(nameof(RepairOrder.InternalNotes)).CurrentValue = internalNotes;
            await context.SaveChangesAsync();
        }

        return new EligibleRepairOrderScenario(location, customer, vehicle, repairOrder);
    }

    public static async Task<Guid> AddEstimateItemAsync(
        IEstimateManagementService service,
        Guid actorUserId,
        Guid estimateId,
        string description,
        decimal quantity,
        decimal unitPrice)
    {
        var result = await service.AddEstimateItemAsync(
            actorUserId,
            new AddEstimateItemCommand
            {
                EstimateId = estimateId,
                Description = description,
                Quantity = quantity,
                UnitPrice = unitPrice,
            });

        Assert.True(result.Success);
        return result.Value!;
    }

    public static async Task<Guid> CreatePresentedEstimateAsync(
        IEstimateManagementService service,
        Guid actorUserId,
        Guid repairOrderId,
        string itemDescription = "Presented item",
        decimal unitPrice = 100m)
    {
        var estimateId = (await service.CreateEstimateAsync(actorUserId, repairOrderId)).Value!;
        await AddEstimateItemAsync(service, actorUserId, estimateId, itemDescription, 1, unitPrice);
        await service.PresentForApprovalAsync(actorUserId, estimateId);
        return estimateId;
    }

    public static async Task<CreatedShareScenario> CreateActiveShareAsync(
        AppDbContext context,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider,
        Guid managerId,
        Guid estimateId,
        int durationDays = EstimateShareExpiryPolicy.DefaultDurationDays)
    {
        var sharingService = CreateSharingService(context, organizationContext, timeProvider);
        var result = await sharingService.CreateShareAsync(
            managerId,
            new CreateEstimateShareCommand { EstimateId = estimateId, DurationDays = durationDays });

        Assert.True(result.Success);
        Assert.NotNull(result.Value);

        return new CreatedShareScenario(
            result.Value.PublicId,
            result.Value.RawToken,
            result.Value.ExpiresAtUtc,
            estimateId);
    }

    public static async Task<ManagerScenario> CreateManagerScenarioAsync(
        DatabaseTransactionScope scope,
        string suffix,
        OrganizationMembershipRole role = OrganizationMembershipRole.Owner)
    {
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, role);
        return new ManagerScenario(organization.Id, manager.Id);
    }

    public static async Task<(ManagerScenario Manager, Guid EstimateId, CreatedShareScenario Share)> CreateSharedPresentedEstimateAsync(
        DatabaseTransactionScope scope,
        string suffix,
        OrganizationMembershipRole role = OrganizationMembershipRole.Owner,
        TimeProvider? timeProvider = null)
    {
        var manager = await CreateManagerScenarioAsync(scope, suffix, role);
        var clock = timeProvider ?? new FakeTimeProvider(DefaultNow);
        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await CreatePresentedEstimateAsync(
            estimateService,
            manager.ManagerId,
            scenario.RepairOrder.Id);
        var share = await CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId);

        return (manager, estimateId, share);
    }

    public static TestOrganizationContextMutator CreateMutator(Guid? organizationId = null)
    {
        var mutator = new TestOrganizationContextMutator();
        if (organizationId.HasValue)
        {
            mutator.Resolve(organizationId.Value);
        }

        return mutator;
    }

    public static void AssertBase64UrlToken(string rawToken)
    {
        Assert.Matches("^[A-Za-z0-9_-]+$", rawToken);
        Assert.Equal(43, rawToken.Length);
    }

    public static void AssertStoredHashOnly(AppDbContext context, string rawToken)
    {
        var expectedHash = EstimateShareTokenHasher.HashToHex(rawToken);
        Assert.Matches("^[0-9a-f]{64}$", expectedHash);

        var share = context.EstimateShares.AsNoTracking().Single();
        Assert.Equal(expectedHash, share.TokenHash);
        Assert.DoesNotContain(rawToken, share.TokenHash, StringComparison.Ordinal);
        Assert.All(context.EstimateShares.AsEnumerable(), candidate =>
            Assert.DoesNotContain(rawToken, candidate.TokenHash, StringComparison.Ordinal));
    }

    public sealed record EligibleRepairOrderScenario(
        WorkshopLocation Location,
        Domain.Customers.Customer Customer,
        Domain.Vehicles.Vehicle Vehicle,
        RepairOrder RepairOrder);

    public sealed record CreatedShareScenario(
        Guid PublicId,
        string RawToken,
        DateTimeOffset ExpiresAtUtc,
        Guid EstimateId);

    public sealed record ManagerScenario(Guid OrganizationId, Guid ManagerId);
}

internal sealed class TestOrganizationContextMutator : IOrganizationContext, IOrganizationContextMutator
{
    public Guid? OrganizationId { get; private set; }

    public bool IsResolved => OrganizationId.HasValue;

    public void Resolve(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization identifier cannot be empty.", nameof(organizationId));
        }

        OrganizationId = organizationId;
    }
}
