using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Catalog;

public sealed class PartCatalogService : IPartCatalogService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;

    public PartCatalogService(AppDbContext dbContext, IOrganizationContext organizationContext)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
    }

    public async Task<PartCatalogListResult> ListPartsAsync(
        PartCatalogListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = NormalizePageSize(query.PageSize);
        var search = CatalogInputValidator.NormalizeSearch(query.Search);

        var parts = _dbContext.PartCatalogItems.AsNoTracking();

        if (query.IsActive.HasValue)
        {
            parts = parts.Where(item => item.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            parts = parts.Where(item =>
                EF.Functions.ILike(item.Sku, pattern)
                || EF.Functions.ILike(item.Name, pattern)
                || (item.Description != null && EF.Functions.ILike(item.Description, pattern)));
        }

        var totalCount = await parts.CountAsync(cancellationToken);

        var items = await parts
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Sku)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new PartCatalogListItem
            {
                PartCatalogItemId = item.Id,
                Sku = item.Sku,
                Name = item.Name,
                DefaultUnitPrice = item.DefaultUnitPrice,
                CurrencyCode = item.CurrencyCode,
                IsActive = item.IsActive,
            })
            .ToListAsync(cancellationToken);

        return new PartCatalogListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<PartCatalogItemDetails?> GetPartDetailsAsync(
        Guid partCatalogItemId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var item = await _dbContext.PartCatalogItems.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == partCatalogItemId, cancellationToken);

        return item is null ? null : MapDetails(item);
    }

    public async Task<CatalogOperationResult<Guid>> CreatePartAsync(
        Guid actorUserId,
        CreatePartCatalogItemCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return CatalogOperationResult<Guid>.Failed(CatalogOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateCatalogManagerAsync(actorUserId, cancellationToken))
        {
            return CatalogOperationResult<Guid>.Failed(CatalogOperationFailureReason.Unauthorized);
        }

        if (!TryNormalizeCreateInput(command, out var sku, out var name, out var description, out var price))
        {
            return CatalogOperationResult<Guid>.Failed(CatalogOperationFailureReason.InvalidInput);
        }

        var organization = await _dbContext.Organizations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == organizationId, cancellationToken);

        var item = new PartCatalogItem(
            organizationId,
            sku,
            name,
            description,
            price,
            organization.DefaultCurrencyCode);

        _dbContext.PartCatalogItems.Add(item);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateSku(exception))
        {
            return CatalogOperationResult<Guid>.Failed(CatalogOperationFailureReason.DuplicateSku);
        }

        return CatalogOperationResult<Guid>.Succeeded(item.Id);
    }

    public async Task<CatalogOperationResult> UpdatePartAsync(
        Guid actorUserId,
        UpdatePartCatalogItemCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateCatalogManagerAsync(actorUserId, cancellationToken))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.Unauthorized);
        }

        if (!TryNormalizeUpdateInput(command, out var sku, out var name, out var description, out var price))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.InvalidInput);
        }

        var item = await _dbContext.PartCatalogItems
            .SingleOrDefaultAsync(candidate => candidate.Id == command.PartCatalogItemId, cancellationToken);

        if (item is null)
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.ItemNotFound);
        }

        if (!command.IsActive && item.IsActive)
        {
            var hasPositiveStock = await _dbContext.PartInventoryBalances.AsNoTracking()
                .AnyAsync(
                    balance => balance.PartCatalogItemId == item.Id && balance.QuantityOnHand > 0,
                    cancellationToken);

            if (hasPositiveStock)
            {
                return CatalogOperationResult.Failed(CatalogOperationFailureReason.HasPositiveStock);
            }
        }

        item.Update(sku, name, description, price, command.IsActive);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateSku(exception))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.DuplicateSku);
        }

        return CatalogOperationResult.Succeeded();
    }

    public async Task<CatalogOperationResult> SetPartActiveStateAsync(
        Guid actorUserId,
        Guid partCatalogItemId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateCatalogManagerAsync(actorUserId, cancellationToken))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.Unauthorized);
        }

        var item = await _dbContext.PartCatalogItems
            .SingleOrDefaultAsync(candidate => candidate.Id == partCatalogItemId, cancellationToken);

        if (item is null)
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.ItemNotFound);
        }

        if (!isActive)
        {
            var hasPositiveStock = await _dbContext.PartInventoryBalances.AsNoTracking()
                .AnyAsync(
                    balance => balance.PartCatalogItemId == item.Id && balance.QuantityOnHand > 0,
                    cancellationToken);

            if (hasPositiveStock)
            {
                return CatalogOperationResult.Failed(CatalogOperationFailureReason.HasPositiveStock);
            }

            item.Deactivate();
        }
        else
        {
            item.Activate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return CatalogOperationResult.Succeeded();
    }

    private static bool TryNormalizeCreateInput(
        CreatePartCatalogItemCommand command,
        out string sku,
        out string name,
        out string? description,
        out decimal price)
    {
        sku = string.Empty;
        name = string.Empty;
        description = null;
        price = 0;

        return CatalogInputValidator.TryNormalizeSku(command.Sku, out sku)
               && CatalogInputValidator.TryNormalizeName(command.Name, out name)
               && CatalogInputValidator.TryNormalizeDescription(command.Description, out description)
               && CatalogInputValidator.IsValidPrice(command.DefaultUnitPrice)
               && (price = command.DefaultUnitPrice) >= 0;
    }

    private static bool TryNormalizeUpdateInput(
        UpdatePartCatalogItemCommand command,
        out string sku,
        out string name,
        out string? description,
        out decimal price)
    {
        sku = string.Empty;
        name = string.Empty;
        description = null;
        price = 0;

        return CatalogInputValidator.TryNormalizeSku(command.Sku, out sku)
               && CatalogInputValidator.TryNormalizeName(command.Name, out name)
               && CatalogInputValidator.TryNormalizeDescription(command.Description, out description)
               && CatalogInputValidator.IsValidPrice(command.DefaultUnitPrice)
               && (price = command.DefaultUnitPrice) >= 0;
    }

    private async Task<bool> ValidateCatalogManagerAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var role = await ResolveMembershipRoleAsync(actorUserId, cancellationToken);
        return CatalogManagerPolicy.CanManageCatalog(role);
    }

    private async Task<OrganizationMembershipRole> ResolveMembershipRoleAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return default;
        }

        return await _dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == actorUserId
                                 && membership.OrganizationId == organizationId
                                 && membership.Status == OrganizationMembershipStatus.Active)
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (_organizationContext.IsResolved && _organizationContext.OrganizationId.HasValue)
        {
            organizationId = _organizationContext.OrganizationId.Value;
            return true;
        }

        organizationId = default;
        return false;
    }

    private static PartCatalogListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<PartCatalogListItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = PartCatalogListQuery.DefaultPageSize,
        };

    private static int NormalizePageSize(int pageSize) =>
        pageSize switch
        {
            < 1 => PartCatalogListQuery.DefaultPageSize,
            > PartCatalogListQuery.MaxPageSize => PartCatalogListQuery.MaxPageSize,
            _ => pageSize,
        };

    private static PartCatalogItemDetails MapDetails(PartCatalogItem item) =>
        new()
        {
            PartCatalogItemId = item.Id,
            Sku = item.Sku,
            Name = item.Name,
            Description = item.Description,
            DefaultUnitPrice = item.DefaultUnitPrice,
            CurrencyCode = item.CurrencyCode,
            IsActive = item.IsActive,
            CreatedAtUtc = item.CreatedAtUtc,
            UpdatedAtUtc = item.UpdatedAtUtc,
        };

    private static bool IsDuplicateSku(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
}
