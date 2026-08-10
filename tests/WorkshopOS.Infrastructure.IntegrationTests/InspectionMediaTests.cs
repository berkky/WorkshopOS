using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkshopOS.Application.InspectionMedia;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.InspectionMedia;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class PhotoTestFixtures
{
    public static readonly byte[] MinimalJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
    ];

    public static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    public static readonly byte[] MinimalWebp =
    [
        0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50, 0x00, 0x00,
    ];
}

[Collection(PostgreSqlCollection.Name)]
public sealed class InspectionMediaTests(PostgreSqlTestFixture fixture)
{
    private static readonly DateTimeOffset DefaultNow = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InspectionMediaAsset_IsTenantFiltered()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.True(AppDbContextModelExtensions.HasNamedOrganizationFilter(context, typeof(InspectionMediaAsset)));
    }

    [Fact]
    public async Task InspectionMedia_ManagerCanUploadValidJpeg() =>
        await AssertManagerUploadSucceeds(PhotoTestFixtures.MinimalJpeg, PhotoUploadPolicy.JpegContentType, ".jpg");

    [Fact]
    public async Task InspectionMedia_ManagerCanUploadValidPng() =>
        await AssertManagerUploadSucceeds(PhotoTestFixtures.MinimalPng, PhotoUploadPolicy.PngContentType, ".png");

    [Fact]
    public async Task InspectionMedia_ManagerCanUploadValidWebP() =>
        await AssertManagerUploadSucceeds(PhotoTestFixtures.MinimalWebp, PhotoUploadPolicy.WebpContentType, ".webp");

    [Fact]
    public async Task InspectionMedia_UploadPersistsServerDetectedContentType()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalPng, PhotoUploadPolicy.PngContentType, "photo.png"));

            Assert.True(result.Success);
            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == result.Value);
            Assert.Equal(PhotoUploadPolicy.PngContentType, asset.ContentType);
        });
    }

    [Fact]
    public async Task InspectionMedia_UploadPersistsSha256()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var content = PhotoTestFixtures.MinimalJpeg;
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, content));

            Assert.True(result.Success);
            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == result.Value);
            Assert.Equal(ComputeSha256Hex(content), asset.Sha256);
        });
    }

    [Fact]
    public async Task InspectionMedia_UploadUsesOpaqueServerStorageKey()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg, declaredFileName: "my-custom-name.jpg"));

            Assert.True(result.Success);
            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == result.Value);
            Assert.DoesNotContain("my-custom-name", asset.StorageKey, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("..", asset.StorageKey, StringComparison.Ordinal);
            Assert.EndsWith(".jpg", asset.StorageKey, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(36, asset.StorageKey.Length);
        });
    }

    [Fact]
    public async Task InspectionMedia_UploadCreatesPrivateStoredFile()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));

            Assert.True(result.Success);
            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == result.Value);
            var storage = CreateStorage(storageRoot);
            Assert.True(await storage.ExistsAsync(asset.StorageKey));
            var storedPath = Path.GetFullPath(Path.Combine(storageRoot, asset.StorageKey));
            Assert.StartsWith(Path.GetFullPath(storageRoot), storedPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsUnsupportedMimeType()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var content = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, content, "application/octet-stream"));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.UnsupportedMediaType, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsSvg()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg, "image/svg+xml", "diagram.svg"));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.UnsupportedMediaType, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsFileWithMismatchedSignature()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg, PhotoUploadPolicy.PngContentType, "photo.png"));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.UnsupportedMediaType, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsFakeJpegExtension()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalPng, PhotoUploadPolicy.JpegContentType, "fake.jpg"));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.UnsupportedMediaType, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsZeroLengthFile()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                new UploadInspectionPhotoCommand
                {
                    InspectionId = inspection.InspectionId,
                    InspectionItemId = inspection.ItemId,
                    Content = new MemoryStream(),
                    DeclaredLength = 0,
                    DeclaredContentType = PhotoUploadPolicy.JpegContentType,
                    DeclaredFileName = "empty.jpg",
                });

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InvalidInput, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsOversizedPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var oversizedLength = PhotoUploadPolicy.MaxPhotoBytes + 1;

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                new UploadInspectionPhotoCommand
                {
                    InspectionId = inspection.InspectionId,
                    InspectionItemId = inspection.ItemId,
                    Content = CreateOversizedJpegStream(oversizedLength),
                    DeclaredLength = oversizedLength,
                    DeclaredContentType = PhotoUploadPolicy.JpegContentType,
                    DeclaredFileName = "large.jpg",
                });

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InvalidInput, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsOtherTenantInspection()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
            var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
            var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

            Guid foreignInspectionId;
            await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
            {
                var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
                await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
                var foreign = await CreateInProgressInspectionAsync(scopeB, organizationB.Id, managerB.Id, suffix);
                foreignInspectionId = foreign.InspectionId;
            }

            await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
            var mediaService = CreateMediaService(writeScopeA, organizationA.Id, storageRoot);
            var local = await CreateInProgressInspectionAsync(writeScopeA, organizationA.Id, managerA.Id, suffix);

            var result = await mediaService.UploadPhotoAsync(
                managerA.Id,
                CreateUploadCommand(foreignInspectionId, local.ItemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InspectionNotFound, result.FailureReason);
            Assert.Equal(0, await writeScopeA.InspectionMediaAssets.CountAsync());
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsOtherTenantInspectionItem()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
            var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
            var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

            Guid foreignItemId;
            await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
            {
                var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
                await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
                var foreign = await CreateInProgressInspectionAsync(scopeB, organizationB.Id, managerB.Id, suffix);
                foreignItemId = foreign.ItemId;
            }

            await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
            var mediaService = CreateMediaService(writeScopeA, organizationA.Id, storageRoot);
            var local = await CreateInProgressInspectionAsync(writeScopeA, organizationA.Id, managerA.Id, suffix);

            var result = await mediaService.UploadPhotoAsync(
                managerA.Id,
                CreateUploadCommand(local.InspectionId, foreignItemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InspectionItemNotFound, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsItemBelongingToDifferentInspection()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var scenario = await CreateManagerScenarioAsync(scope, suffix);
            await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), new FakeTimeProvider(DefaultNow));
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var inspectionA = await CreateInProgressInspectionAsync(writeScope, scenario.OrganizationId, scenario.ManagerId, $"{suffix}-a");
            var inspectionB = await CreateInProgressInspectionAsync(writeScope, scenario.OrganizationId, scenario.ManagerId, $"{suffix}-b");

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspectionA.InspectionId, inspectionB.ItemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InspectionItemNotFound, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_UploadRequiresInProgressInspection()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var scenario = await CreateManagerScenarioAsync(scope, suffix);
            await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), new FakeTimeProvider(DefaultNow));
            var inspectionService = CreateInspectionService(writeScope, scenario.OrganizationId);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var repairOrder = await CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, suffix);
            var inspectionId = (await inspectionService.CreateInspectionAsync(scenario.ManagerId, repairOrder.RepairOrder.Id)).Value!;
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsCompletedInspectionUpload()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var scenario = await CreateManagerScenarioAsync(scope, suffix);
            await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), new FakeTimeProvider(DefaultNow));
            var inspectionService = CreateInspectionService(writeScope, scenario.OrganizationId);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var repairOrder = await CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, suffix);
            var inspectionId = await CreateCompletedInspectionAsync(inspectionService, scenario.ManagerId, repairOrder.RepairOrder.Id, writeScope);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsMoreThanPerItemPhotoLimit()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            for (var index = 0; index < PhotoUploadPolicy.MaxPhotosPerItem; index++)
            {
                var upload = await mediaService.UploadPhotoAsync(
                    scenario.ManagerId,
                    CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));
                Assert.True(upload.Success);
            }

            var overflow = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(overflow.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.PhotoLimitExceeded, overflow.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RejectsMoreThanPerInspectionPhotoLimit()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var itemIds = await writeScope.InspectionItems
                .Where(candidate => candidate.InspectionId == inspection.InspectionId)
                .OrderBy(candidate => candidate.SortOrder)
                .Select(candidate => candidate.Id)
                .ToListAsync();

            var uploaded = 0;
            foreach (var itemId in itemIds)
            {
                for (var photoIndex = 0; photoIndex < 2 && uploaded < PhotoUploadPolicy.MaxPhotosPerInspection; photoIndex++)
                {
                    var upload = await mediaService.UploadPhotoAsync(
                        scenario.ManagerId,
                        CreateUploadCommand(inspection.InspectionId, itemId, PhotoTestFixtures.MinimalJpeg));
                    Assert.True(upload.Success);
                    uploaded++;
                }
            }

            Assert.Equal(PhotoUploadPolicy.MaxPhotosPerInspection, uploaded);

            var overflow = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, itemIds[0], PhotoTestFixtures.MinimalJpeg));

            Assert.False(overflow.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.PhotoLimitExceeded, overflow.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_RemovedPhotosDoNotCountAgainstLimits()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            Guid? firstMediaId = null;

            for (var index = 0; index < PhotoUploadPolicy.MaxPhotosPerItem; index++)
            {
                var upload = await mediaService.UploadPhotoAsync(
                    scenario.ManagerId,
                    CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));
                Assert.True(upload.Success);
                firstMediaId ??= upload.Value;
            }

            Assert.True((await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = firstMediaId!.Value })).Success);

            var replacement = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));
            Assert.True(replacement.Success);
        });
    }

    [Fact]
    public async Task InspectionMedia_AssignedTechnicianCanUpload()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
            var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
            var assigned = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, suffix, technicianUser.Id);
            var inspectionService = CreateInspectionService(writeScope, organization.Id);
            var mediaService = CreateMediaService(writeScope, organization.Id, storageRoot);
            var inspectionId = (await inspectionService.CreateInspectionAsync(manager.Id, assigned.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(technicianUser.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                technicianUser.Id,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.True(result.Success);
        });
    }

    [Fact]
    public async Task InspectionMedia_TechnicianCannotUploadForAnotherTechniciansRepairOrder()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
            var technicianA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
            var technicianB = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-b");

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
            var scenarioA = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, $"{suffix}-a", technicianA.Id);
            await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, $"{suffix}-b", technicianB.Id);
            var inspectionService = CreateInspectionService(writeScope, organization.Id);
            var mediaService = CreateMediaService(writeScope, organization.Id, storageRoot);
            var inspectionId = (await inspectionService.CreateInspectionAsync(manager.Id, scenarioA.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(manager.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                technicianB.Id,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.Unauthorized, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_UnlinkedTechnicianCannotUpload()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
            var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
            var repairOrder = await CreateEligibleRepairOrderAsync(writeScope, organization.Id, suffix);
            var unlinkedTechnician = await TestDataFactory.PersistTechnicianAtLocationAsync(writeScope, organization.Id, repairOrder.Location.Id, $"{suffix}-unlinked");
            await TestDataFactory.PersistMembershipAsync(writeScope, organization.Id, technicianUser.Id, OrganizationMembershipRole.Technician);
            await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(writeScope, organization.Id, repairOrder.RepairOrder.Id, unlinkedTechnician.StaffMember.Id, DefaultNow);
            var inspectionService = CreateInspectionService(writeScope, organization.Id);
            var mediaService = CreateMediaService(writeScope, organization.Id, storageRoot);
            var inspectionId = (await inspectionService.CreateInspectionAsync(manager.Id, repairOrder.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(manager.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                technicianUser.Id,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.TechnicianProfileNotLinked, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_InactiveStaffCannotUpload()
    {
        await AssertTechnicianUploadFailsAsync(
            (writeScope, organizationId, suffix, technicianUserId) =>
                CreateAssignedTechnicianScenarioAsync(writeScope, organizationId, suffix, technicianUserId, staffStatus: StaffStatus.Inactive),
            InspectionMediaOperationFailureReason.StaffInactive);
    }

    [Fact]
    public async Task InspectionMedia_SuspendedMembershipCannotUpload()
    {
        await AssertTechnicianUploadFailsAsync(
            (writeScope, organizationId, suffix, technicianUserId) =>
                CreateAssignedTechnicianScenarioAsync(writeScope, organizationId, suffix, technicianUserId, membershipStatus: OrganizationMembershipStatus.Suspended),
            InspectionMediaOperationFailureReason.MembershipInactive);
    }

    [Fact]
    public async Task InspectionMedia_RevokedMembershipCannotUpload()
    {
        await AssertTechnicianUploadFailsAsync(
            (writeScope, organizationId, suffix, technicianUserId) =>
                CreateAssignedTechnicianScenarioAsync(writeScope, organizationId, suffix, technicianUserId, membershipStatus: OrganizationMembershipStatus.Revoked),
            InspectionMediaOperationFailureReason.MembershipInactive);
    }

    [Fact]
    public async Task InspectionMedia_OwnerWithTechnicianProfileCanUploadOwnAssignedEvidence()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var ownerTechnician = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
            var assigned = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, suffix, ownerTechnician.Id, membershipRole: OrganizationMembershipRole.Owner);
            var inspectionService = CreateInspectionService(writeScope, organization.Id);
            var mediaService = CreateMediaService(writeScope, organization.Id, storageRoot);
            var inspectionId = (await inspectionService.CreateInspectionAsync(ownerTechnician.Id, assigned.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(ownerTechnician.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                ownerTechnician.Id,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.True(result.Success);
        });
    }

    [Fact]
    public async Task InspectionMedia_ManagerCanRemoveInProgressPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = mediaId });

            Assert.True(result.Success);
        });
    }

    [Fact]
    public async Task InspectionMedia_AssignedTechnicianCanRemoveOwnRepairOrderPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
            var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
            var assigned = await CreateAssignedTechnicianScenarioAsync(writeScope, organization.Id, suffix, technicianUser.Id);
            var inspectionService = CreateInspectionService(writeScope, organization.Id);
            var mediaService = CreateMediaService(writeScope, organization.Id, storageRoot);
            var inspectionId = (await inspectionService.CreateInspectionAsync(manager.Id, assigned.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(technicianUser.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();
            var upload = await mediaService.UploadPhotoAsync(technicianUser.Id, CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));
            Assert.True(upload.Success);

            var result = await mediaService.RemovePhotoAsync(
                technicianUser.Id,
                new RemoveInspectionPhotoCommand { InspectionId = inspectionId, MediaId = upload.Value! });

            Assert.True(result.Success);
        });
    }

    [Fact]
    public async Task InspectionMedia_RemoveMarksMetadataRemoved()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            Assert.True((await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = mediaId })).Success);

            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == mediaId);
            Assert.NotNull(asset.RemovedAtUtc);
            Assert.Equal(scenario.ManagerId, asset.RemovedByUserId);
        });
    }

    [Fact]
    public async Task InspectionMedia_RemoveMakesContentUnavailable()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == mediaId);
            var storage = CreateStorage(storageRoot);

            Assert.True((await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = mediaId })).Success);

            Assert.Null(await mediaService.GetPhotoContentAsync(mediaId));
            Assert.False(await storage.ExistsAsync(asset.StorageKey));
        });
    }

    [Fact]
    public async Task InspectionMedia_RemovedPhotoNotIncludedInActiveProjection()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var inspectionService = CreateInspectionService(writeScope, scenario.OrganizationId);

            Assert.True((await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = mediaId })).Success);

            var details = await inspectionService.GetInspectionDetailsAsync(inspection.InspectionId, scenario.ManagerId);
            Assert.NotNull(details);
            Assert.Equal(0, details!.ActivePhotoCount);
            Assert.All(details.Sections.SelectMany(section => section.Items), item => Assert.Empty(item.Media));
        });
    }

    [Fact]
    public async Task InspectionMedia_RemovedPhotoDoesNotCountAgainstLimits()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var inspectionService = CreateInspectionService(writeScope, scenario.OrganizationId);

            Assert.True((await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = mediaId })).Success);

            var details = await inspectionService.GetInspectionDetailsAsync(inspection.InspectionId, scenario.ManagerId);
            Assert.NotNull(details);
            Assert.Equal(0, details!.ActivePhotoCount);

            var replacement = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));
            Assert.True(replacement.Success);
        });
    }

    [Fact]
    public async Task InspectionMedia_CannotRemoveCompletedInspectionPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var scenario = await CreateManagerScenarioAsync(scope, suffix);
            await using var writeScope = scope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), new FakeTimeProvider(DefaultNow));
            var inspectionService = CreateInspectionService(writeScope, scenario.OrganizationId);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var repairOrder = await CreateEligibleRepairOrderAsync(writeScope, scenario.OrganizationId, suffix);
            var inspectionId = (await inspectionService.CreateInspectionAsync(scenario.ManagerId, repairOrder.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(scenario.ManagerId, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();
            var upload = await mediaService.UploadPhotoAsync(scenario.ManagerId, CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));
            Assert.True(upload.Success);
            await MarkAllItemsInspectedAsync(inspectionService, scenario.ManagerId, inspectionId, writeScope);
            await inspectionService.CompleteInspectionAsync(scenario.ManagerId, inspectionId);

            var result = await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspectionId, MediaId = upload.Value! });

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InvalidLifecycleTransition, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_CannotRemoveOtherTenantPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
            var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");
            var managerA = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-a");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organizationA.Id, managerA.Id, OrganizationMembershipRole.Owner);

            Guid foreignInspectionId;
            Guid foreignMediaId;
            await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
            {
                var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
                await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
                var mediaServiceB = CreateMediaService(scopeB, organizationB.Id, storageRoot);
                var foreign = await CreateInProgressInspectionAsync(scopeB, organizationB.Id, managerB.Id, suffix);
                foreignInspectionId = foreign.InspectionId;
                var upload = await mediaServiceB.UploadPhotoAsync(managerB.Id, CreateUploadCommand(foreign.InspectionId, foreign.ItemId, PhotoTestFixtures.MinimalJpeg));
                Assert.True(upload.Success);
                foreignMediaId = upload.Value!;
            }

            await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
            var mediaServiceA = CreateMediaService(writeScopeA, organizationA.Id, storageRoot);

            var result = await mediaServiceA.RemovePhotoAsync(
                managerA.Id,
                new RemoveInspectionPhotoCommand { InspectionId = foreignInspectionId, MediaId = foreignMediaId });

            Assert.False(result.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.InspectionNotFound, result.FailureReason);
        });
    }

    [Fact]
    public async Task InspectionMedia_Content_ReturnsCurrentTenantPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var content = await mediaService.GetPhotoContentAsync(mediaId);
            Assert.NotNull(content);
            using (content!.Content)
            {
                using var reader = new MemoryStream();
                await content.Content.CopyToAsync(reader);
                Assert.Equal(PhotoTestFixtures.MinimalJpeg, reader.ToArray());
            }
        });
    }

    [Fact]
    public async Task InspectionMedia_Content_DoesNotExposeOtherTenantPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organizationA = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-a");
            var organizationB = await TestDataFactory.PersistOrganizationAsync(scope.Context, $"{suffix}-b");

            Guid foreignMediaId;
            await using (var scopeB = scope.CreateContext(new TestOrganizationContext(organizationB.Id), new FakeTimeProvider(DefaultNow)))
            {
                var managerB = await TestDataFactory.PersistUserAsync(scopeB, $"{suffix}-b");
                await TestDataFactory.PersistMembershipAsync(scopeB, organizationB.Id, managerB.Id, OrganizationMembershipRole.Owner);
                var mediaServiceB = CreateMediaService(scopeB, organizationB.Id, storageRoot);
                var foreign = await CreateInProgressInspectionAsync(scopeB, organizationB.Id, managerB.Id, suffix);
                var upload = await mediaServiceB.UploadPhotoAsync(managerB.Id, CreateUploadCommand(foreign.InspectionId, foreign.ItemId, PhotoTestFixtures.MinimalJpeg));
                Assert.True(upload.Success);
                foreignMediaId = upload.Value!;
            }

            await using var writeScopeA = scope.CreateContext(new TestOrganizationContext(organizationA.Id), new FakeTimeProvider(DefaultNow));
            var mediaServiceA = CreateMediaService(writeScopeA, organizationA.Id, storageRoot);
            Assert.Null(await mediaServiceA.GetPhotoContentAsync(foreignMediaId));
        });
    }

    [Fact]
    public async Task InspectionMedia_Content_RejectsRemovedPhoto()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection, mediaId) = await UploadDefaultPhotoAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            Assert.True((await mediaService.RemovePhotoAsync(
                scenario.ManagerId,
                new RemoveInspectionPhotoCommand { InspectionId = inspection.InspectionId, MediaId = mediaId })).Success);
            Assert.Null(await mediaService.GetPhotoContentAsync(mediaId));
        });
    }

    [Fact]
    public async Task InspectionMedia_Content_UsesServerStoredContentType()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var upload = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalPng, PhotoUploadPolicy.PngContentType, "photo.png"));
            Assert.True(upload.Success);

            var content = await mediaService.GetPhotoContentAsync(upload.Value!);
            Assert.NotNull(content);
            Assert.Equal(PhotoUploadPolicy.PngContentType, content!.ContentType);
        });
    }

    [Fact]
    public async Task InspectionMedia_Storage_ClientFilenameCannotInfluencePath()
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
            var upload = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg, declaredFileName: "../../evil.jpg"));
            Assert.True(upload.Success);

            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == upload.Value);
            var storedPath = Path.GetFullPath(Path.Combine(storageRoot, asset.StorageKey));
            Assert.StartsWith(Path.GetFullPath(storageRoot), storedPath, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.jpg")));
            Assert.True(File.Exists(storedPath));
        });
    }

    [Fact]
    public async Task InspectionMedia_Storage_CreateNewPreventsOverwrite()
    {
        var storageRoot = CreateStorageRoot();
        try
        {
            var storage = CreateStorage(storageRoot);
            var key = $"{Guid.CreateVersion7():N}.jpg";
            await using (var first = new MemoryStream(PhotoTestFixtures.MinimalJpeg))
            {
                await storage.WriteAsync(first, key);
            }

            await using var second = new MemoryStream(PhotoTestFixtures.MinimalPng);
            await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(second, key));
        }
        finally
        {
            TryDeleteStorageRoot(storageRoot);
        }
    }

    private async Task AssertManagerUploadSucceeds(byte[] content, string contentType, string expectedExtension)
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
            var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);

            var result = await mediaService.UploadPhotoAsync(
                scenario.ManagerId,
                CreateUploadCommand(inspection.InspectionId, inspection.ItemId, content, contentType, $"photo{expectedExtension}"));

            Assert.True(result.Success);
            var asset = await writeScope.InspectionMediaAssets.SingleAsync(candidate => candidate.Id == result.Value);
            Assert.EndsWith(expectedExtension, asset.StorageKey, StringComparison.OrdinalIgnoreCase);
        });
    }

    private async Task AssertTechnicianUploadFailsAsync(
        Func<AppDbContext, Guid, string, Guid, Task<AssignedTechnicianScenario>> scenarioFactory,
        InspectionMediaOperationFailureReason expectedReason)
    {
        await WithStorageRootAsync(async storageRoot =>
        {
            var suffix = Guid.CreateVersion7().ToString("N");
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
            var technicianUser = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-tech");

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(DefaultNow));
            var assigned = await scenarioFactory(writeScope, organization.Id, suffix, technicianUser.Id);
            var inspectionService = CreateInspectionService(writeScope, organization.Id);
            var mediaService = CreateMediaService(writeScope, organization.Id, storageRoot);
            var inspectionId = (await inspectionService.CreateInspectionAsync(manager.Id, assigned.RepairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(manager.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var result = await mediaService.UploadPhotoAsync(
                technicianUser.Id,
                CreateUploadCommand(inspectionId, itemId, PhotoTestFixtures.MinimalJpeg));

            Assert.False(result.Success);
            Assert.Equal(expectedReason, result.FailureReason);
        });
    }

    private async Task WithStorageRootAsync(Func<string, Task> test)
    {
        var storageRoot = CreateStorageRoot();
        try
        {
            await test(storageRoot);
        }
        finally
        {
            if (_activeScope is not null)
            {
                await _activeScope.DisposeAsync();
                _activeScope = null;
            }

            TryDeleteStorageRoot(storageRoot);
        }
    }

    private async Task<(AppDbContext WriteScope, ManagerScenario Scenario, InspectionContext Inspection)> CreateDefaultManagerInspectionAsync(string storageRoot)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        _activeScope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
        var scenario = await CreateManagerScenarioAsync(_activeScope, suffix);
        var writeScope = _activeScope.CreateContext(new TestOrganizationContext(scenario.OrganizationId), new FakeTimeProvider(DefaultNow));
        var inspection = await CreateInProgressInspectionAsync(writeScope, scenario.OrganizationId, scenario.ManagerId, suffix);
        return (writeScope, scenario, inspection);
    }

    private async Task<(AppDbContext WriteScope, ManagerScenario Scenario, InspectionContext Inspection, Guid MediaId)> UploadDefaultPhotoAsync(string storageRoot)
    {
        var (writeScope, scenario, inspection) = await CreateDefaultManagerInspectionAsync(storageRoot);
        var mediaService = CreateMediaService(writeScope, scenario.OrganizationId, storageRoot);
        var upload = await mediaService.UploadPhotoAsync(
            scenario.ManagerId,
            CreateUploadCommand(inspection.InspectionId, inspection.ItemId, PhotoTestFixtures.MinimalJpeg));
        Assert.True(upload.Success);
        return (writeScope, scenario, inspection, upload.Value!);
    }

    private DatabaseTransactionScope? _activeScope;

    private static string CreateStorageRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshopos-media-test", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteStorageRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FileSystemInspectionMediaStorage CreateStorage(string storageRoot) =>
        new(Options.Create(new InspectionMediaStorageOptions { StorageRootPath = storageRoot }));

    private static IInspectionMediaService CreateMediaService(AppDbContext context, Guid organizationId, string storageRoot) =>
        new InspectionMediaService(
            context,
            new TestOrganizationContext(organizationId),
            CreateStorage(storageRoot),
            new FakeTimeProvider(DefaultNow));

    private static IInspectionManagementService CreateInspectionService(AppDbContext context, Guid organizationId) =>
        new InspectionManagementService(context, new TestOrganizationContext(organizationId), new FakeTimeProvider(DefaultNow));

    private static UploadInspectionPhotoCommand CreateUploadCommand(
        Guid inspectionId,
        Guid inspectionItemId,
        byte[] content,
        string? declaredContentType = PhotoUploadPolicy.JpegContentType,
        string? declaredFileName = "photo.jpg") =>
        new()
        {
            InspectionId = inspectionId,
            InspectionItemId = inspectionItemId,
            Content = new MemoryStream(content),
            DeclaredLength = content.Length,
            DeclaredContentType = declaredContentType,
            DeclaredFileName = declaredFileName,
        };

    private static Stream CreateOversizedJpegStream(long totalLength)
    {
        var stream = new MemoryStream();
        stream.Write(PhotoTestFixtures.MinimalJpeg);
        stream.Write(new byte[totalLength - PhotoTestFixtures.MinimalJpeg.Length]);
        stream.Position = 0;
        return stream;
    }

    private static string ComputeSha256Hex(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static async Task<ManagerScenario> CreateManagerScenarioAsync(DatabaseTransactionScope scope, string suffix)
    {
        var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
        var manager = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-manager");
        await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, manager.Id, OrganizationMembershipRole.Owner);
        return new ManagerScenario(organization.Id, manager.Id);
    }

    private static async Task<InspectionContext> CreateInProgressInspectionAsync(
        AppDbContext context,
        Guid organizationId,
        Guid managerId,
        string suffix)
    {
        var inspectionService = CreateInspectionService(context, organizationId);
        var repairOrder = await CreateEligibleRepairOrderAsync(context, organizationId, suffix);
        var inspectionId = (await inspectionService.CreateInspectionAsync(managerId, repairOrder.RepairOrder.Id)).Value!;
        await inspectionService.StartInspectionAsync(managerId, inspectionId);
        var itemId = await context.InspectionItems
            .Where(candidate => candidate.InspectionId == inspectionId)
            .Select(candidate => candidate.Id)
            .FirstAsync();
        return new InspectionContext(inspectionId, itemId);
    }

    private static async Task<EligibleRepairOrderScenario> CreateEligibleRepairOrderAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        RepairOrderStatus status = RepairOrderStatus.Draft)
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
            status);
        return new EligibleRepairOrderScenario(location, repairOrder);
    }

    private static async Task<AssignedTechnicianScenario> CreateAssignedTechnicianScenarioAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        Guid technicianUserId,
        OrganizationMembershipStatus membershipStatus = OrganizationMembershipStatus.Active,
        OrganizationMembershipRole membershipRole = OrganizationMembershipRole.Technician,
        StaffStatus staffStatus = StaffStatus.Active)
    {
        var scenario = await CreateEligibleRepairOrderAsync(context, organizationId, suffix);
        await TestDataFactory.PersistMembershipAsync(context, organizationId, technicianUserId, membershipRole, membershipStatus);
        var technician = await TestDataFactory.PersistTechnicianAtLocationAsync(
            context,
            organizationId,
            scenario.Location.Id,
            $"{suffix}-linked",
            technicianUserId,
            staffStatus);
        await TestDataFactory.PersistRepairOrderTechnicianAssignmentAsync(
            context,
            organizationId,
            scenario.RepairOrder.Id,
            technician.StaffMember.Id,
            DefaultNow);
        return new AssignedTechnicianScenario(scenario.Location, scenario.RepairOrder, technician);
    }

    private static async Task<Guid> CreateCompletedInspectionAsync(
        IInspectionManagementService service,
        Guid actorUserId,
        Guid repairOrderId,
        AppDbContext context)
    {
        var inspectionId = (await service.CreateInspectionAsync(actorUserId, repairOrderId)).Value!;
        await service.StartInspectionAsync(actorUserId, inspectionId);
        await MarkAllItemsInspectedAsync(service, actorUserId, inspectionId, context);
        await service.CompleteInspectionAsync(actorUserId, inspectionId);
        return inspectionId;
    }

    private static async Task MarkAllItemsInspectedAsync(
        IInspectionManagementService service,
        Guid actorUserId,
        Guid inspectionId,
        AppDbContext context)
    {
        var items = await context.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).ToListAsync();
        await service.UpdateInspectionItemsAsync(
            actorUserId,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = items.Select(item => new InspectionItemUpdate
                {
                    InspectionItemId = item.Id,
                    Condition = InspectionCondition.Good,
                }).ToList(),
            });
    }

    private sealed record ManagerScenario(Guid OrganizationId, Guid ManagerId);
    private sealed record InspectionContext(Guid InspectionId, Guid ItemId);

    private sealed record EligibleRepairOrderScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder);

    private sealed record AssignedTechnicianScenario(
        Domain.Organizations.WorkshopLocation Location,
        RepairOrder RepairOrder,
        (StaffMember StaffMember, StaffLocationAssignment LocationAssignment) Technician);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class InspectionMediaManagerAuthorizationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task InspectionMedia_ManagerAllowsOwner() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Owner, shouldSucceed: true);

    [Fact]
    public async Task InspectionMedia_ManagerAllowsAdministrator() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Administrator, shouldSucceed: true);

    [Fact]
    public async Task InspectionMedia_ManagerAllowsServiceAdvisor() =>
        await AssertRoleAllowed(OrganizationMembershipRole.ServiceAdvisor, shouldSucceed: true);

    [Fact]
    public async Task InspectionMedia_ManagerRejectsTechnicianRoleWithoutAssignment() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Technician, shouldSucceed: false);

    [Fact]
    public async Task InspectionMedia_ManagerRejectsViewer() =>
        await AssertRoleAllowed(OrganizationMembershipRole.Viewer, shouldSucceed: false);

    [Fact]
    public async Task InspectionMedia_ManagerReflectsDatabaseRoleChange()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var storageRoot = Path.Combine(Path.GetTempPath(), "workshopos-media-test", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        try
        {
            await using var scope = await fixture.BeginScopeAsync(new UnresolvedOrganizationContext());
            var organization = await TestDataFactory.PersistOrganizationAsync(scope.Context, suffix);
            var owner = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-owner");
            var advisor = await TestDataFactory.PersistUserAsync(scope.Context, $"{suffix}-advisor");
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, owner.Id, OrganizationMembershipRole.Owner);
            await TestDataFactory.PersistMembershipAsync(scope.Context, organization.Id, advisor.Id, OrganizationMembershipRole.ServiceAdvisor);

            await using var writeScope = scope.CreateContext(new TestOrganizationContext(organization.Id), new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));
            var inspectionService = new InspectionManagementService(writeScope, new TestOrganizationContext(organization.Id), new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));
            var mediaService = new InspectionMediaService(
                writeScope,
                new TestOrganizationContext(organization.Id),
                new FileSystemInspectionMediaStorage(Options.Create(new InspectionMediaStorageOptions { StorageRootPath = storageRoot })),
                new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));

            var location = await TestDataFactory.PersistWorkshopLocationAsync(writeScope, organization.Id, suffix);
            var customer = await TestDataFactory.PersistCustomerAsync(writeScope, organization.Id, suffix);
            var vehicle = await TestDataFactory.PersistVehicleAsync(writeScope, organization.Id, suffix, customer.Id);
            var repairOrder = await TestDataFactory.PersistRepairOrderAsync(writeScope, organization.Id, location.Id, customer.Id, vehicle.Id, suffix, new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
            var inspectionId = (await inspectionService.CreateInspectionAsync(owner.Id, repairOrder.Id)).Value!;
            await inspectionService.StartInspectionAsync(advisor.Id, inspectionId);
            var itemId = await writeScope.InspectionItems.Where(candidate => candidate.InspectionId == inspectionId).Select(candidate => candidate.Id).FirstAsync();

            var initialUpload = await mediaService.UploadPhotoAsync(
                advisor.Id,
                new UploadInspectionPhotoCommand
                {
                    InspectionId = inspectionId,
                    InspectionItemId = itemId,
                    Content = new MemoryStream(PhotoTestFixtures.MinimalJpeg),
                    DeclaredLength = PhotoTestFixtures.MinimalJpeg.Length,
                    DeclaredContentType = PhotoUploadPolicy.JpegContentType,
                    DeclaredFileName = "photo.jpg",
                });
            Assert.True(initialUpload.Success);

            var advisorMembership = await writeScope.OrganizationMemberships.SingleAsync(membership => membership.UserId == advisor.Id);
            advisorMembership.ChangeRole(OrganizationMembershipRole.Technician);
            await writeScope.SaveChangesAsync();

            var afterUpload = await mediaService.UploadPhotoAsync(
                advisor.Id,
                new UploadInspectionPhotoCommand
                {
                    InspectionId = inspectionId,
                    InspectionItemId = itemId,
                    Content = new MemoryStream(PhotoTestFixtures.MinimalJpeg),
                    DeclaredLength = PhotoTestFixtures.MinimalJpeg.Length,
                    DeclaredContentType = PhotoUploadPolicy.JpegContentType,
                    DeclaredFileName = "photo.jpg",
                });
            Assert.False(afterUpload.Success);
            Assert.Equal(InspectionMediaOperationFailureReason.TechnicianProfileNotLinked, afterUpload.FailureReason);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
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
        var handler = new InspectionManagerAuthorizationHandler(writeScope, organizationContext);

        var context = new AuthorizationHandlerContext(
            [new InspectionManagerRequirement()],
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString())],
                authenticationType: "Test")),
            resource: null);

        await handler.HandleAsync(context);
        Assert.Equal(shouldSucceed, context.HasSucceeded);
    }
}
