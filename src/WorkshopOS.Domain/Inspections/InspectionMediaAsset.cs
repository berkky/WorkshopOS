using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Inspections;

public class InspectionMediaAsset : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid InspectionId { get; private set; }

    public Guid InspectionItemId { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public InspectionMediaKind MediaKind { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public long LengthBytes { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public string? Caption { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public DateTimeOffset? RemovedAtUtc { get; private set; }

    public Guid? RemovedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected InspectionMediaAsset()
    {
    }

    public InspectionMediaAsset(
        Guid organizationId,
        Guid inspectionId,
        Guid inspectionItemId,
        string storageKey,
        InspectionMediaKind mediaKind,
        string contentType,
        long lengthBytes,
        string sha256,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAtUtc,
        string? caption = null)
        : base(organizationId)
    {
        if (inspectionId == Guid.Empty)
        {
            throw new ArgumentException("Inspection identifier is required.", nameof(inspectionId));
        }

        if (inspectionItemId == Guid.Empty)
        {
            throw new ArgumentException("Inspection item identifier is required.", nameof(inspectionItemId));
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        if (storageKey.IndexOfAny(PathSeparators) >= 0)
        {
            throw new ArgumentException("Storage key must be path-safe.", nameof(storageKey));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (lengthBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthBytes), "Length must be positive.");
        }

        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
        {
            throw new ArgumentException("SHA-256 hash must be 64 hex characters.", nameof(sha256));
        }

        if (uploadedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Uploader user identifier is required.", nameof(uploadedByUserId));
        }

        InspectionId = inspectionId;
        InspectionItemId = inspectionItemId;
        StorageKey = storageKey.Trim();
        MediaKind = mediaKind;
        ContentType = contentType.Trim();
        LengthBytes = lengthBytes;
        Sha256 = sha256.ToLowerInvariant();
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = uploadedAtUtc;
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
    }

    public void UpdateCaption(string? caption)
    {
        if (RemovedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Removed media cannot be modified.");
        }

        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
    }

    public void MarkRemoved(Guid removedByUserId, DateTimeOffset removedAtUtc)
    {
        if (removedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Remover user identifier is required.", nameof(removedByUserId));
        }

        if (RemovedAtUtc.HasValue)
        {
            return;
        }

        RemovedAtUtc = removedAtUtc;
        RemovedByUserId = removedByUserId;
    }

    public bool IsActive => !RemovedAtUtc.HasValue;

    private static readonly char[] PathSeparators = ['/', '\\'];
}
