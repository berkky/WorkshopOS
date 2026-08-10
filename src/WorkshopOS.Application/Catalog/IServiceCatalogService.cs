namespace WorkshopOS.Application.Catalog;

public interface IServiceCatalogService
{
    Task<ServiceCatalogListResult> ListServicesAsync(
        ServiceCatalogListQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceCatalogItemDetails?> GetServiceDetailsAsync(
        Guid serviceCatalogItemId,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult<Guid>> CreateServiceAsync(
        Guid actorUserId,
        CreateServiceCatalogItemCommand command,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult> UpdateServiceAsync(
        Guid actorUserId,
        UpdateServiceCatalogItemCommand command,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult> SetServiceActiveStateAsync(
        Guid actorUserId,
        Guid serviceCatalogItemId,
        bool isActive,
        CancellationToken cancellationToken = default);
}

public sealed record ServiceCatalogListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class ServiceCatalogListResult
{
    public required IReadOnlyList<ServiceCatalogListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class ServiceCatalogListItem
{
    public required Guid ServiceCatalogItemId { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required decimal DefaultUnitPrice { get; init; }

    public required string CurrencyCode { get; init; }

    public required bool IsActive { get; init; }
}

public sealed class ServiceCatalogItemDetails
{
    public required Guid ServiceCatalogItemId { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal DefaultUnitPrice { get; init; }

    public required string CurrencyCode { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record CreateServiceCatalogItemCommand
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal DefaultUnitPrice { get; init; }
}

public sealed record UpdateServiceCatalogItemCommand
{
    public required Guid ServiceCatalogItemId { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal DefaultUnitPrice { get; init; }

    public required bool IsActive { get; init; }
}

public sealed class CatalogOperationResult
{
    public bool Success { get; init; }

    public CatalogOperationFailureReason? FailureReason { get; init; }

    public static CatalogOperationResult Succeeded() => new() { Success = true };

    public static CatalogOperationResult Failed(CatalogOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public sealed class CatalogOperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public CatalogOperationFailureReason? FailureReason { get; init; }

    public static CatalogOperationResult<T> Succeeded(T value) =>
        new() { Success = true, Value = value };

    public static CatalogOperationResult<T> Failed(CatalogOperationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

public enum CatalogOperationFailureReason
{
    OrganizationUnresolved = 1,
    Unauthorized = 2,
    InvalidInput = 3,
    ItemNotFound = 4,
    DuplicateCode = 5,
    DuplicateSku = 6,
    HasPositiveStock = 7,
    ConcurrencyConflict = 8,
}
