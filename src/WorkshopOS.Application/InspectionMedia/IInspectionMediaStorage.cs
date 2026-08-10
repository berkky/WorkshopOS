namespace WorkshopOS.Application.InspectionMedia;

public interface IInspectionMediaStorage
{
    Task<InspectionMediaStorageWriteResult> WriteAsync(
        Stream content,
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record InspectionMediaStorageWriteResult(long LengthBytes, string Sha256Hex);
