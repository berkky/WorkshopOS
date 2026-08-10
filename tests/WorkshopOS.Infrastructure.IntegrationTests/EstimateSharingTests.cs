using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.EstimateSharing;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateShareTenantIsolationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void EstimateShare_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(EstimateShare)));
    }

    [Fact]
    public async Task EstimateShare_IsTenantFiltered_DoesNotExposeOtherOrganizationShares()
    {
        var suffix = Guid.CreateVersion7().ToString("N");

        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        var managerB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);

        Guid shareBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(EstimateShareTestSupport.DefaultNow)))
        {
            var estimateService = EstimateShareTestSupport.CreateEstimateService(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(EstimateShareTestSupport.DefaultNow));
            var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(
                estimateService,
                managerB.Id,
                scenario.RepairOrder.Id);
            await EstimateShareTestSupport.CreateActiveShareAsync(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(EstimateShareTestSupport.DefaultNow),
                managerB.Id,
                estimateId);
            shareBId = await scopeB.EstimateShares.Select(candidate => candidate.Id).SingleAsync();
        }

        await using var readScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id));
        Assert.Equal(0, await readScopeA.EstimateShares.CountAsync());
        Assert.Null(await readScopeA.EstimateShares.SingleOrDefaultAsync(candidate => candidate.Id == shareBId));
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateSharingTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = EstimateShareTestSupport.DefaultNow;

    [Fact]
    public void EstimateShare_TokenHasAtLeast256BitsEntropyRepresentation()
    {
        var generator = new EstimateShareTokenGenerator();
        var rawToken = generator.GenerateRawToken();

        EstimateShareTestSupport.AssertBase64UrlToken(rawToken);

        var decoded = DecodeBase64Url(rawToken);
        Assert.Equal(32, decoded.Length);
    }

    [Fact]
    public async Task EstimateShare_RawTokenIsNotPersisted()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);

        await using var readScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId));
        var stored = await readScope.EstimateShares.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(share.RawToken, stored.TokenHash, StringComparison.Ordinal);
        Assert.False(await readScope.EstimateShares.AnyAsync(candidate => EF.Functions.ILike(candidate.TokenHash, $"%{share.RawToken}%")));
    }

    [Fact]
    public async Task EstimateShare_PersistsOnlySha256Hash()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);

        await using var readScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId));
        EstimateShareTestSupport.AssertStoredHashOnly(readScope, share.RawToken);
    }

    [Fact]
    public async Task EstimateShare_TokenHashIsUnique()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock);
        var scenarioA = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-a");
        var scenarioB = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, $"{suffix}-b");
        var estimateAId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenarioA.RepairOrder.Id);
        var estimateBId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenarioB.RepairOrder.Id);

        await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateAId);
        await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateBId);

        var hashes = await writeScope.EstimateShares.Select(candidate => candidate.TokenHash).ToListAsync();
        Assert.Equal(2, hashes.Count);
        Assert.Equal(2, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EstimateShare_PublicIdIsServerGenerated()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, _, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);

        await using var readScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId));
        var stored = await readScope.EstimateShares.AsNoTracking().SingleAsync();
        Assert.Equal(share.PublicId, stored.PublicId);
        Assert.NotEqual(Guid.Empty, stored.PublicId);
    }

    [Fact]
    public async Task EstimateShare_ClientCannotControlPublicId()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);

        await using var readScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId));
        var stored = await readScope.EstimateShares.AsNoTracking().SingleAsync(candidate => candidate.EstimateId == estimateId);
        Assert.Equal(share.PublicId, stored.PublicId);
        Assert.NotEqual(Guid.Parse("11111111-1111-7111-8111-111111111111"), stored.PublicId);
    }

    [Fact]
    public async Task EstimateShare_Create_UsesCurrentOrganization()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, _) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);

        await using var readScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId));
        var share = await readScope.EstimateShares.SingleAsync();
        Assert.Equal(manager.OrganizationId, share.OrganizationId);
        Assert.Equal(estimateId, share.EstimateId);
    }

    [Fact]
    public async Task EstimateShare_Create_WhenOrganizationUnresolvedFailsClosed()
    {
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var service = EstimateShareTestSupport.CreateSharingService(
            scope.Context,
            new UnresolvedOrganizationContext(),
            new FakeTimeProvider(DefaultNow));

        var result = await service.CreateShareAsync(
            Guid.CreateVersion7(),
            new CreateEstimateShareCommand { EstimateId = Guid.CreateVersion7() });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.OrganizationUnresolved, result.FailureReason);
        Assert.Equal(0, await scope.Context.EstimateShares.CountAsync());
    }

    [Fact]
    public async Task EstimateShare_Create_RejectsOtherTenantEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var estimateService = EstimateShareTestSupport.CreateEstimateService(scopeB, new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow));
            var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            estimateBId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, managerB.Id, scenario.RepairOrder.Id);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var sharingService = EstimateShareTestSupport.CreateSharingService(
            writeScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));

        var result = await sharingService.CreateShareAsync(
            managerA.Id,
            new CreateEstimateShareCommand { EstimateId = estimateBId });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.EstimateNotFound, result.FailureReason);
        Assert.Equal(0, await writeScopeA.EstimateShares.CountAsync());
    }

    [Fact]
    public async Task EstimateShare_Create_RejectsDraftEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = (await estimateService.CreateEstimateAsync(manager.ManagerId, scenario.RepairOrder.Id)).Value!;
        await EstimateShareTestSupport.AddEstimateItemAsync(estimateService, manager.ManagerId, estimateId, "Draft item", 1, 50m);

        var sharingService = EstimateShareTestSupport.CreateSharingService(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock);
        var result = await sharingService.CreateShareAsync(
            manager.ManagerId,
            new CreateEstimateShareCommand { EstimateId = estimateId });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.EstimateNotEligible, result.FailureReason);
    }

    [Fact]
    public async Task EstimateShare_Create_AllowsSentEstimate()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);

        Assert.NotEqual(Guid.Empty, share.PublicId);
        await using var readScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId));
        Assert.Equal(1, await readScope.EstimateShares.CountAsync(candidate => candidate.EstimateId == estimateId));
    }

    [Fact]
    public async Task EstimateShare_Create_ServerControlsExpiry()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        const int durationDays = 14;
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var share = await EstimateShareTestSupport.CreateActiveShareAsync(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            manager.ManagerId,
            estimateId,
            durationDays);

        Assert.Equal(DefaultNow.AddDays(durationDays), share.ExpiresAtUtc);
    }

    [Fact]
    public async Task EstimateShare_Create_EnforcesMaximumExpiry()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var manager = await EstimateShareTestSupport.CreateManagerScenarioAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, manager.OrganizationId, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, manager.ManagerId, scenario.RepairOrder.Id);
        var sharingService = EstimateShareTestSupport.CreateSharingService(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock);

        var result = await sharingService.CreateShareAsync(
            manager.ManagerId,
            new CreateEstimateShareCommand
            {
                EstimateId = estimateId,
                DurationDays = EstimateShareExpiryPolicy.MaximumDurationDays + 1,
            });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.InvalidInput, result.FailureReason);
    }

    [Fact]
    public async Task EstimateShare_RotateRevokesPreviousShare()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, initialShare) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock,
            new EstimateShareTokenGenerator());

        var rotateResult = await sharingService.RotateShareAsync(
            manager.ManagerId,
            new RotateEstimateShareCommand { EstimateId = estimateId });

        Assert.True(rotateResult.Success);
        var shares = await writeScope.EstimateShares.OrderBy(candidate => candidate.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, shares.Count);
        Assert.NotNull(shares[0].RevokedAtUtc);
        Assert.Null(shares[1].RevokedAtUtc);
        Assert.NotEqual(initialShare.RawToken, rotateResult.Value!.RawToken);
    }

    [Fact]
    public async Task EstimateShare_RotateCreatesNewTokenHash()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, initialShare) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(
            writeScope,
            new TestOrganizationContext(manager.OrganizationId),
            clock);

        var rotateResult = await sharingService.RotateShareAsync(
            manager.ManagerId,
            new RotateEstimateShareCommand { EstimateId = estimateId });

        Assert.True(rotateResult.Success);
        var activeShare = await writeScope.EstimateShares.SingleAsync(candidate => candidate.RevokedAtUtc == null);
        var activeHash = EstimateShareTokenHasher.HashToHex(rotateResult.Value!.RawToken);
        Assert.Equal(activeHash, activeShare.TokenHash);
        Assert.NotEqual(EstimateShareTokenHasher.HashToHex(initialShare.RawToken), activeShare.TokenHash);
    }

    [Fact]
    public async Task EstimateShare_OldTokenFailsAfterRotation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, initialShare) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        await sharingService.RotateShareAsync(manager.ManagerId, new RotateEstimateShareCommand { EstimateId = estimateId });

        var exchangeResult = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = initialShare.PublicId,
            RawToken = initialShare.RawToken,
        });

        Assert.False(exchangeResult.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, exchangeResult.FailureReason);
    }

    [Fact]
    public async Task EstimateShare_NewTokenSucceedsAfterRotation()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, _) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);

        var rotateResult = await sharingService.RotateShareAsync(
            manager.ManagerId,
            new RotateEstimateShareCommand { EstimateId = estimateId });

        var exchangeResult = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = rotateResult.Value!.PublicId,
            RawToken = rotateResult.Value.RawToken,
        });

        Assert.True(exchangeResult.Success);
    }

    [Fact]
    public async Task EstimateShare_PreviousRowIsPreserved()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, initialShare) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        await sharingService.RotateShareAsync(manager.ManagerId, new RotateEstimateShareCommand { EstimateId = estimateId });

        var shares = await writeScope.EstimateShares.AsNoTracking().ToListAsync();
        Assert.Equal(2, shares.Count);
        Assert.Contains(shares, candidate => candidate.PublicId == initialShare.PublicId && candidate.RevokedAtUtc.HasValue);
    }

    [Fact]
    public async Task EstimateShare_ManagerCanRevoke()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, _) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var result = await sharingService.RevokeShareAsync(
            manager.ManagerId,
            new RevokeEstimateShareCommand { EstimateId = estimateId });

        Assert.True(result.Success);
        var share = await writeScope.EstimateShares.SingleAsync();
        Assert.NotNull(share.RevokedAtUtc);
        Assert.Equal(manager.ManagerId, share.RevokedByUserId);
    }

    [Fact]
    public async Task EstimateShare_RevokePreservesRow()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        Assert.Equal(1, await writeScope.EstimateShares.CountAsync());
        var stored = await writeScope.EstimateShares.SingleAsync();
        Assert.Equal(share.PublicId, stored.PublicId);
    }

    [Fact]
    public async Task EstimateShare_RevokedShareCannotExchangeToken()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, share) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);
        var mutator = EstimateShareTestSupport.CreateMutator();

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        var portalService = EstimateShareTestSupport.CreatePortalService(writeScope, mutator, clock);
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var exchangeResult = await portalService.ExchangeTokenAsync(new ExchangeEstimateShareTokenCommand
        {
            PublicId = share.PublicId,
            RawToken = share.RawToken,
        });

        Assert.False(exchangeResult.Success);
        Assert.Equal(CustomerPortalOperationFailureReason.InvalidAccess, exchangeResult.FailureReason);
    }

    [Fact]
    public async Task EstimateShare_RevokeDoesNotChangeEstimateStatus()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var (manager, estimateId, _) = await EstimateShareTestSupport.CreateSharedPresentedEstimateAsync(scope, suffix);
        var clock = new FakeTimeProvider(DefaultNow);

        await using var writeScope = scope.CreateContext(new TestOrganizationContext(manager.OrganizationId), clock);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, new TestOrganizationContext(manager.OrganizationId), clock);
        await sharingService.RevokeShareAsync(manager.ManagerId, new RevokeEstimateShareCommand { EstimateId = estimateId });

        var estimate = await writeScope.Estimates.SingleAsync(candidate => candidate.Id == estimateId);
        Assert.Equal(EstimateStatus.Sent, estimate.Status);
    }

    [Fact]
    public async Task EstimateShare_CannotRevokeOtherTenantShare()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
        var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
        var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

        Guid estimateBId;
        await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
        {
            var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
            await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
            var estimateService = EstimateShareTestSupport.CreateEstimateService(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(DefaultNow));
            var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(scopeB, organizationB.Id, suffix);
            estimateBId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, managerB.Id, scenario.RepairOrder.Id);
            await EstimateShareTestSupport.CreateActiveShareAsync(
                scopeB,
                new TestOrganizationContext(organizationB.Id),
                new FakeTimeProvider(DefaultNow),
                managerB.Id,
                estimateBId);
        }

        await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
        var sharingService = EstimateShareTestSupport.CreateSharingService(
            writeScopeA,
            new TestOrganizationContext(organizationA.Id),
            new FakeTimeProvider(DefaultNow));
        var result = await sharingService.RevokeShareAsync(
            managerA.Id,
            new RevokeEstimateShareCommand { EstimateId = estimateBId });

        Assert.False(result.Success);
        Assert.Equal(EstimateShareOperationFailureReason.ShareNotFound, result.FailureReason);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class EstimateShareManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task EstimateShareManager_AllowsOwner() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);

    [Fact]
    public async Task EstimateShareManager_AllowsAdministrator() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);

    [Fact]
    public async Task EstimateShareManager_AllowsServiceAdvisor() =>
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);

    [Fact]
    public async Task EstimateShareManager_RejectsTechnician() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);

    [Fact]
    public async Task EstimateShareManager_RejectsViewer() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);

    [Fact]
    public async Task EstimateShareManager_ReflectsDatabaseRoleChange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var advisor = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-advisor");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(
            scope.Context,
            organization.Id,
            advisor.Id,
            OrganizationMembershipRole.ServiceAdvisor);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var clock = new FakeTimeProvider(EstimateShareTestSupport.DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, advisor.Id, scenario.RepairOrder.Id);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, organizationContext, clock);

        var initialResult = await sharingService.CreateShareAsync(
            advisor.Id,
            new CreateEstimateShareCommand { EstimateId = estimateId });
        Assert.True(initialResult.Success);

        var advisorMembership = await writeScope.OrganizationMemberships
            .SingleAsync(membership => membership.UserId == advisor.Id);
        advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
        await writeScope.SaveChangesAsync();

        var afterResult = await sharingService.RevokeShareAsync(
            advisor.Id,
            new RevokeEstimateShareCommand { EstimateId = estimateId });
        Assert.False(afterResult.Success);
        Assert.Equal(EstimateShareOperationFailureReason.Unauthorized, afterResult.FailureReason);
    }

    private async Task AssertRoleAllowed(OrganizationMembershipRole role, bool shouldSucceed)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
        var actor = await TestDataFactory.PersistUserAsync(scope.Context, suffix);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, actor.Id, role);

        var organizationContext = new TestOrganizationContext(organization.Id);
        var clock = new FakeTimeProvider(EstimateShareTestSupport.DefaultNow);
        await using var writeScope = scope.CreateContext(organizationContext, clock);
        var estimateService = EstimateShareTestSupport.CreateEstimateService(writeScope, organizationContext, clock);
        var scenario = await EstimateShareTestSupport.CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
        var estimateId = await EstimateShareTestSupport.CreatePresentedEstimateAsync(estimateService, owner.Id, scenario.RepairOrder.Id);
        var sharingService = EstimateShareTestSupport.CreateSharingService(writeScope, organizationContext, clock);

        var result = await sharingService.CreateShareAsync(
            actor.Id,
            new CreateEstimateShareCommand { EstimateId = estimateId });

        Assert.Equal(shouldSucceed, result.Success);
        if (!shouldSucceed)
        {
            Assert.Equal(EstimateShareOperationFailureReason.Unauthorized, result.FailureReason);
        }

        Assert.Equal(shouldSucceed, EstimateShareManagerPolicy.CanManageEstimateShares(role));
    }
}
