using Microsoft.Extensions.Options;
using WorkshopOS.Application.InspectionMedia;

namespace WorkshopOS.Infrastructure.InspectionMedia;

public sealed class InspectionMediaStorageOptions
{
    public const string SectionName = "InspectionMedia";

    public string? StorageRootPath { get; set; }
}

public sealed class FileSystemInspectionMediaStorage : IInspectionMediaStorage
{
    private readonly string _storageRoot;

    public FileSystemInspectionMediaStorage(IOptions<InspectionMediaStorageOptions> options)
    {
        var configuredPath = options.Value.StorageRootPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("Inspection media storage root path is not configured.");
        }

        _storageRoot = Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<InspectionMediaStorageWriteResult> WriteAsync(
        Stream content,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);
        var path = GetSafeFilePath(storageKey);

        await using var fileStream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous);

        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > PhotoUploadPolicy.MaxPhotoBytes)
            {
                await fileStream.DisposeAsync();
                TryDeleteFile(path);
                throw new InvalidOperationException("Photo exceeds maximum allowed size.");
            }

            hash.AppendData(buffer, 0, read);
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (totalBytes == 0)
        {
            await fileStream.DisposeAsync();
            TryDeleteFile(path);
            throw new InvalidOperationException("Photo content is empty.");
        }

        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        return new InspectionMediaStorageWriteResult(totalBytes, sha256);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);
        var path = GetSafeFilePath(storageKey);

        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);
        var path = GetSafeFilePath(storageKey);
        return Task.FromResult(TryDeleteFile(path));
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);
        var path = GetSafeFilePath(storageKey);
        return Task.FromResult(File.Exists(path));
    }

    private string GetSafeFilePath(string storageKey)
    {
        var combined = Path.GetFullPath(Path.Combine(_storageRoot, storageKey));
        if (!IsSubPathOf(combined, _storageRoot))
        {
            throw new InvalidOperationException("Storage key resolves outside the configured storage root.");
        }

        return combined;
    }

    private static void ValidateStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || storageKey.IndexOfAny(['/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || storageKey.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key is not path-safe.", nameof(storageKey));
        }
    }

    private static bool TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static bool IsSubPathOf(string path, string parentPath)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
