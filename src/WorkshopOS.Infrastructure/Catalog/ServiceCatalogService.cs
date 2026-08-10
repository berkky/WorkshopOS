using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Domain.Catalog;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Catalog;

public sealed class ServiceCatalogService : IServiceCatalogService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;

    public ServiceCatalogService(AppDbContext dbContext, IOrganizationContext organizationContext)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
    }

    public async Task<ServiceCatalogListResult> ListServicesAsync(
        ServiceCatalogListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = NormalizePageSize(query.PageSize);
        var search = CatalogInputValidator.NormalizeSearch(query.Search);

        var services = _dbContext.ServiceCatalogItems.AsNoTracking();

        if (query.IsActive.HasValue)
        {
            services = services.Where(item => item.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            services = services.Where(item =>
                EF.Functions.ILike(item.Code, pattern)
                || EF.Functions.ILike(item.Name, pattern)
                || (item.Description != null && EF.Functions.ILike(item.Description, pattern)));
        }

        var totalCount = await services.CountAsync(cancellationToken);

        var items = await services
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new ServiceCatalogListItem
            {
                ServiceCatalogItemId = item.Id,
                Code = item.Code,
                Name = item.Name,
                DefaultUnitPrice = item.DefaultUnitPrice,
                CurrencyCode = item.CurrencyCode,
                IsActive = item.IsActive,
            })
            .ToListAsync(cancellationToken);

        return new ServiceCatalogListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<ServiceCatalogItemDetails?> GetServiceDetailsAsync(
        Guid serviceCatalogItemId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var item = await _dbContext.ServiceCatalogItems.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == serviceCatalogItemId, cancellationToken);

        return item is null ? null : MapDetails(item);
    }

    public async Task<CatalogOperationResult<Guid>> CreateServiceAsync(
        Guid actorUserId,
        CreateServiceCatalogItemCommand command,
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

        if (!TryNormalizeCreateInput(command, out var code, out var name, out var description, out var price))
        {
            return CatalogOperationResult<Guid>.Failed(CatalogOperationFailureReason.InvalidInput);
        }

        var organization = await _dbContext.Organizations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == organizationId, cancellationToken);

        var item = new ServiceCatalogItem(
            organizationId,
            code,
            name,
            description,
            price,
            organization.DefaultCurrencyCode);

        _dbContext.ServiceCatalogItems.Add(item);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateCode(exception))
        {
            return CatalogOperationResult<Guid>.Failed(CatalogOperationFailureReason.DuplicateCode);
        }

        return CatalogOperationResult<Guid>.Succeeded(item.Id);
    }

    public async Task<CatalogOperationResult> UpdateServiceAsync(
        Guid actorUserId,
        UpdateServiceCatalogItemCommand command,
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

        if (!TryNormalizeUpdateInput(command, out var code, out var name, out var description, out var price))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.InvalidInput);
        }

        var item = await _dbContext.ServiceCatalogItems
            .SingleOrDefaultAsync(candidate => candidate.Id == command.ServiceCatalogItemId, cancellationToken);

        if (item is null)
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.ItemNotFound);
        }

        item.Update(code, name, description, price, command.IsActive);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateCode(exception))
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.DuplicateCode);
        }

        return CatalogOperationResult.Succeeded();
    }

    public async Task<CatalogOperationResult> SetServiceActiveStateAsync(
        Guid actorUserId,
        Guid serviceCatalogItemId,
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

        var item = await _dbContext.ServiceCatalogItems
            .SingleOrDefaultAsync(candidate => candidate.Id == serviceCatalogItemId, cancellationToken);

        if (item is null)
        {
            return CatalogOperationResult.Failed(CatalogOperationFailureReason.ItemNotFound);
        }

        if (isActive)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return CatalogOperationResult.Succeeded();
    }

    private static bool TryNormalizeCreateInput(
        CreateServiceCatalogItemCommand command,
        out string code,
        out string name,
        out string? description,
        out decimal price)
    {
        code = string.Empty;
        name = string.Empty;
        description = null;
        price = 0;

        return CatalogInputValidator.TryNormalizeCode(command.Code, out code)
               && CatalogInputValidator.TryNormalizeName(command.Name, out name)
               && CatalogInputValidator.TryNormalizeDescription(command.Description, out description)
               && CatalogInputValidator.IsValidPrice(command.DefaultUnitPrice)
               && (price = command.DefaultUnitPrice) >= 0;
    }

    private static bool TryNormalizeUpdateInput(
        UpdateServiceCatalogItemCommand command,
        out string code,
        out string name,
        out string? description,
        out decimal price)
    {
        code = string.Empty;
        name = string.Empty;
        description = null;
        price = 0;

        return CatalogInputValidator.TryNormalizeCode(command.Code, out code)
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

    private static ServiceCatalogListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<ServiceCatalogListItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = ServiceCatalogListQuery.DefaultPageSize,
        };

    private static int NormalizePageSize(int pageSize) =>
        pageSize switch
        {
            < 1 => ServiceCatalogListQuery.DefaultPageSize,
            > ServiceCatalogListQuery.MaxPageSize => ServiceCatalogListQuery.MaxPageSize,
            _ => pageSize,
        };

    private static ServiceCatalogItemDetails MapDetails(ServiceCatalogItem item) =>
        new()
        {
            ServiceCatalogItemId = item.Id,
            Code = item.Code,
            Name = item.Name,
            Description = item.Description,
            DefaultUnitPrice = item.DefaultUnitPrice,
            CurrencyCode = item.CurrencyCode,
            IsActive = item.IsActive,
            CreatedAtUtc = item.CreatedAtUtc,
            UpdatedAtUtc = item.UpdatedAtUtc,
        };

    private static bool IsDuplicateCode(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
}
