namespace WorkshopOS.Application.Catalog;

public interface IPartCatalogService
{
    Task<PartCatalogListResult> ListPartsAsync(
        PartCatalogListQuery query,
        CancellationToken cancellationToken = default);

    Task<PartCatalogItemDetails?> GetPartDetailsAsync(
        Guid partCatalogItemId,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult<Guid>> CreatePartAsync(
        Guid actorUserId,
        CreatePartCatalogItemCommand command,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult> UpdatePartAsync(
        Guid actorUserId,
        UpdatePartCatalogItemCommand command,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult> SetPartActiveStateAsync(
        Guid actorUserId,
        Guid partCatalogItemId,
        bool isActive,
        CancellationToken cancellationToken = default);
}

public sealed record PartCatalogListQuery
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed class PartCatalogListResult
{
    public required IReadOnlyList<PartCatalogListItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class PartCatalogListItem
{
    public required Guid PartCatalogItemId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required decimal DefaultUnitPrice { get; init; }

    public required string CurrencyCode { get; init; }

    public required bool IsActive { get; init; }
}

public sealed class PartCatalogItemDetails
{
    public required Guid PartCatalogItemId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal DefaultUnitPrice { get; init; }

    public required string CurrencyCode { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record CreatePartCatalogItemCommand
{
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal DefaultUnitPrice { get; init; }
}

public sealed record UpdatePartCatalogItemCommand
{
    public required Guid PartCatalogItemId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal DefaultUnitPrice { get; init; }

    public required bool IsActive { get; init; }
}
