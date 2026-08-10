using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.InspectionMedia;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.InspectionMedia;

public sealed class InspectionMediaService : IInspectionMediaService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly IInspectionMediaStorage _mediaStorage;
    private readonly TimeProvider _timeProvider;

    public InspectionMediaService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        IInspectionMediaStorage mediaStorage,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _mediaStorage = mediaStorage;
        _timeProvider = timeProvider;
    }

    public async Task<InspectionMediaOperationResult<Guid>> UploadPhotoAsync(
        Guid actorUserId,
        UploadInspectionPhotoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.OrganizationUnresolved);
        }

        if (command.DeclaredLength <= 0 || command.DeclaredLength > PhotoUploadPolicy.MaxPhotoBytes)
        {
            return InspectionMediaOperationResult<Guid>.Failed(InspectionMediaOperationFailureReason.InvalidInput);
        }

        if (PhotoSignatureValidator.IsSvgContentType(command.DeclaredContentType)
            || PhotoSignatureValidator.IsSvgFileName(command.DeclaredFileName))
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.UnsupportedMediaType);
        }

        if (command.Caption is { Length: > PhotoUploadPolicy.MaxCaptionLength })
        {
            return InspectionMediaOperationResult<Guid>.Failed(InspectionMediaOperationFailureReason.InvalidInput);
        }

        var inspection = await _dbContext.Inspections
            .SingleOrDefaultAsync(candidate => candidate.Id == command.InspectionId, cancellationToken);

        if (inspection is null)
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.InspectionNotFound);
        }

        if (!IsUploadAllowedStatus(inspection.Status))
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.InvalidLifecycleTransition);
        }

        var execution = await ValidateMutationAsync(actorUserId, inspection.RepairOrderId, cancellationToken);
        if (!execution.Success)
        {
            return InspectionMediaOperationResult<Guid>.Failed(execution.FailureReason!.Value);
        }

        var item = await _dbContext.InspectionItems
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.InspectionItemId
                               && candidate.InspectionId == command.InspectionId,
                cancellationToken);

        if (item is null)
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.InspectionItemNotFound);
        }

        var itemPhotoCount = await CountActivePhotosForItemAsync(
            command.InspectionId,
            command.InspectionItemId,
            cancellationToken);

        if (itemPhotoCount >= PhotoUploadPolicy.MaxPhotosPerItem)
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.PhotoLimitExceeded);
        }

        var inspectionPhotoCount = await CountActivePhotosForInspectionAsync(command.InspectionId, cancellationToken);
        if (inspectionPhotoCount >= PhotoUploadPolicy.MaxPhotosPerInspection)
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.PhotoLimitExceeded);
        }

        var header = new byte[16];
        var headerRead = await command.Content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (headerRead == 0)
        {
            return InspectionMediaOperationResult<Guid>.Failed(InspectionMediaOperationFailureReason.InvalidInput);
        }

        var detectedFormat = PhotoSignatureValidator.DetectFormat(header.AsSpan(0, headerRead));
        if (detectedFormat == DetectedPhotoFormat.None)
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.UnsupportedMediaType);
        }

        if (!IsDeclaredTypeCompatible(command.DeclaredContentType, detectedFormat))
        {
            return InspectionMediaOperationResult<Guid>.Failed(
                InspectionMediaOperationFailureReason.UnsupportedMediaType);
        }

        var contentType = PhotoSignatureValidator.ToContentType(detectedFormat);
        var extension = PhotoSignatureValidator.ToFileExtension(detectedFormat);
        var storageKey = $"{Guid.CreateVersion7():N}{extension}";

        await using var uploadStream = new PrefixedStream(header.AsMemory(0, headerRead), command.Content);
        InspectionMediaStorageWriteResult writeResult;
        try
        {
            writeResult = await _mediaStorage.WriteAsync(uploadStream, storageKey, cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase))
        {
            return InspectionMediaOperationResult<Guid>.Failed(InspectionMediaOperationFailureReason.InvalidInput);
        }
        catch (IOException)
        {
            return InspectionMediaOperationResult<Guid>.Failed(InspectionMediaOperationFailureReason.StorageConflict);
        }

        try
        {
            var asset = new InspectionMediaAsset(
                organizationId,
                command.InspectionId,
                command.InspectionItemId,
                storageKey,
                InspectionMediaKind.Photo,
                contentType,
                writeResult.LengthBytes,
                writeResult.Sha256Hex,
                actorUserId,
                _timeProvider.GetUtcNow(),
                command.Caption);

            _dbContext.InspectionMediaAssets.Add(asset);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return InspectionMediaOperationResult<Guid>.Succeeded(asset.Id);
        }
        catch (DbUpdateException)
        {
            await _mediaStorage.DeleteAsync(storageKey, cancellationToken);
            return InspectionMediaOperationResult<Guid>.Failed(InspectionMediaOperationFailureReason.StorageConflict);
        }
    }

    public async Task<InspectionMediaOperationResult> RemovePhotoAsync(
        Guid actorUserId,
        RemoveInspectionPhotoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return InspectionMediaOperationResult.Failed(
                InspectionMediaOperationFailureReason.OrganizationUnresolved);
        }

        var inspection = await _dbContext.Inspections
            .SingleOrDefaultAsync(candidate => candidate.Id == command.InspectionId, cancellationToken);

        if (inspection is null)
        {
            return InspectionMediaOperationResult.Failed(InspectionMediaOperationFailureReason.InspectionNotFound);
        }

        if (!IsUploadAllowedStatus(inspection.Status))
        {
            return InspectionMediaOperationResult.Failed(
                InspectionMediaOperationFailureReason.InvalidLifecycleTransition);
        }

        var execution = await ValidateMutationAsync(actorUserId, inspection.RepairOrderId, cancellationToken);
        if (!execution.Success)
        {
            return InspectionMediaOperationResult.Failed(execution.FailureReason!.Value);
        }

        var asset = await _dbContext.InspectionMediaAssets
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.MediaId && candidate.InspectionId == command.InspectionId,
                cancellationToken);

        if (asset is null || !asset.IsActive)
        {
            return InspectionMediaOperationResult.Failed(InspectionMediaOperationFailureReason.MediaNotFound);
        }

        asset.MarkRemoved(actorUserId, _timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _mediaStorage.DeleteAsync(asset.StorageKey, cancellationToken);
        return InspectionMediaOperationResult.Succeeded();
    }

    public async Task<InspectionMediaContent?> GetPhotoContentAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var asset = await _dbContext.InspectionMediaAssets.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == mediaId, cancellationToken);

        if (asset is null || !asset.IsActive)
        {
            return null;
        }

        var stream = await _mediaStorage.OpenReadAsync(asset.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        return new InspectionMediaContent
        {
            MediaId = asset.Id,
            ContentType = asset.ContentType,
            Content = stream,
            DownloadFileName = $"inspection-photo-{asset.Id}{PhotoSignatureValidator.ToFileExtension(MapContentType(asset.ContentType))}",
        };
    }

    private async Task<int> CountActivePhotosForItemAsync(
        Guid inspectionId,
        Guid inspectionItemId,
        CancellationToken cancellationToken) =>
        await _dbContext.InspectionMediaAssets.AsNoTracking()
            .CountAsync(
                candidate => candidate.InspectionId == inspectionId
                               && candidate.InspectionItemId == inspectionItemId
                               && candidate.RemovedAtUtc == null,
                cancellationToken);

    private async Task<int> CountActivePhotosForInspectionAsync(
        Guid inspectionId,
        CancellationToken cancellationToken) =>
        await _dbContext.InspectionMediaAssets.AsNoTracking()
            .CountAsync(
                candidate => candidate.InspectionId == inspectionId && candidate.RemovedAtUtc == null,
                cancellationToken);

    private async Task<(bool Success, InspectionMediaOperationFailureReason? FailureReason)> ValidateMutationAsync(
        Guid actorUserId,
        Guid repairOrderId,
        CancellationToken cancellationToken)
    {
        if (await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return (true, null);
        }

        if (!TryGetOrganizationId(out var organizationId))
        {
            return (false, InspectionMediaOperationFailureReason.OrganizationUnresolved);
        }

        var membership = await _dbContext.OrganizationMemberships.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == actorUserId && candidate.OrganizationId == organizationId,
                cancellationToken);

        if (membership is null || membership.Status != OrganizationMembershipStatus.Active)
        {
            return (false, InspectionMediaOperationFailureReason.MembershipInactive);
        }

        var linkedStaff = await _dbContext.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == actorUserId, cancellationToken);

        if (linkedStaff is null)
        {
            return (false, InspectionMediaOperationFailureReason.TechnicianProfileNotLinked);
        }

        if (linkedStaff.Status != StaffStatus.Active)
        {
            return (false, InspectionMediaOperationFailureReason.StaffInactive);
        }

        if (linkedStaff.Position != StaffPosition.Technician)
        {
            return (false, InspectionMediaOperationFailureReason.Unauthorized);
        }

        var isAssigned = await _dbContext.RepairOrderTechnicianAssignments.AnyAsync(
            assignment => assignment.RepairOrderId == repairOrderId
                          && assignment.StaffMemberId == linkedStaff.Id
                          && assignment.UnassignedAtUtc == null,
            cancellationToken);

        return isAssigned
            ? (true, null)
            : (false, InspectionMediaOperationFailureReason.Unauthorized);
    }

    private async Task<bool> ValidateManagerAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return false;
        }

        var role = await _dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == actorUserId
                                 && membership.OrganizationId == organizationId
                                 && membership.Status == OrganizationMembershipStatus.Active)
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return InspectionManagerPolicy.CanManageInspections(role);
    }

    private static bool IsUploadAllowedStatus(InspectionStatus status) =>
        status == InspectionStatus.InProgress;

    private static bool IsDeclaredTypeCompatible(string? declaredContentType, DetectedPhotoFormat detectedFormat)
    {
        if (string.IsNullOrWhiteSpace(declaredContentType))
        {
            return true;
        }

        if (PhotoSignatureValidator.IsSvgContentType(declaredContentType))
        {
            return false;
        }

        var expected = PhotoSignatureValidator.ToContentType(detectedFormat);
        return string.Equals(declaredContentType, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static DetectedPhotoFormat MapContentType(string contentType) =>
        contentType switch
        {
            PhotoUploadPolicy.JpegContentType => DetectedPhotoFormat.Jpeg,
            PhotoUploadPolicy.PngContentType => DetectedPhotoFormat.Png,
            PhotoUploadPolicy.WebpContentType => DetectedPhotoFormat.Webp,
            _ => DetectedPhotoFormat.None,
        };

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (_organizationContext.IsResolved && _organizationContext.OrganizationId.HasValue)
        {
            organizationId = _organizationContext.OrganizationId.Value;
            return true;
        }

        organizationId = Guid.Empty;
        return false;
    }

    private sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
    {
        private ReadOnlyMemory<byte> _prefix = prefix;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (!_prefix.IsEmpty)
            {
                var copied = Math.Min(buffer.Length, _prefix.Length);
                _prefix.Span[..copied].CopyTo(buffer);
                _prefix = _prefix[copied..];
                return copied;
            }

            return inner.Read(buffer);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_prefix.IsEmpty)
            {
                var copied = Math.Min(buffer.Length, _prefix.Length);
                _prefix.Span[..copied].CopyTo(buffer.Span);
                _prefix = _prefix[copied..];
                return copied;
            }

            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
