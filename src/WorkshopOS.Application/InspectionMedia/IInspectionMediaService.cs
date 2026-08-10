namespace WorkshopOS.Application.InspectionMedia;

public interface IInspectionMediaService
{
    Task<InspectionMediaOperationResult<Guid>> UploadPhotoAsync(
        Guid actorUserId,
        UploadInspectionPhotoCommand command,
        CancellationToken cancellationToken = default);

    Task<InspectionMediaOperationResult> RemovePhotoAsync(
        Guid actorUserId,
        RemoveInspectionPhotoCommand command,
        CancellationToken cancellationToken = default);

    Task<InspectionMediaContent?> GetPhotoContentAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default);
}

public sealed record UploadInspectionPhotoCommand
{
    public required Guid InspectionId { get; init; }

    public required Guid InspectionItemId { get; init; }

    public required Stream Content { get; init; }

    public required long DeclaredLength { get; init; }

    public string? DeclaredContentType { get; init; }

    public string? DeclaredFileName { get; init; }

    public string? Caption { get; init; }
}

public sealed record RemoveInspectionPhotoCommand
{
    public required Guid InspectionId { get; init; }

    public required Guid MediaId { get; init; }
}

public sealed class InspectionMediaContent
{
    public required Guid MediaId { get; init; }

    public required string ContentType { get; init; }

    public required Stream Content { get; init; }

    public required string DownloadFileName { get; init; }
}

public sealed class InspectionMediaOperationResult
{
    public bool Success { get; init; }

    public InspectionMediaOperationFailureReason? FailureReason { get; init; }

    public static InspectionMediaOperationResult Succeeded() => new() { Success = true };

    public static InspectionMediaOperationResult Failed(InspectionMediaOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class InspectionMediaOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public InspectionMediaOperationFailureReason? FailureReason { get; init; }

    public static InspectionMediaOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static InspectionMediaOperationResult<T> Failed(InspectionMediaOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum InspectionMediaOperationFailureReason
{
    OrganizationUnresolved = 1,
    InspectionNotFound = 2,
    InspectionItemNotFound = 3,
    MediaNotFound = 4,
    Unauthorized = 5,
    InvalidInput = 6,
    InvalidLifecycleTransition = 7,
    UnsupportedMediaType = 8,
    PhotoLimitExceeded = 9,
    StorageConflict = 10,
    StorageUnavailable = 11,
    MembershipInactive = 12,
    TechnicianProfileNotLinked = 13,
    StaffInactive = 14,
}
