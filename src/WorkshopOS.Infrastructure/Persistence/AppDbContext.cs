using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Common;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public const string OrganizationFilterName = "OrganizationFilter";

    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
        : base(options)
    {
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<WorkshopLocation> WorkshopLocations => Set<WorkshopLocation>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<RepairOrder> RepairOrders => Set<RepairOrder>();

    public DbSet<Inspection> Inspections => Set<Inspection>();

    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();

    public DbSet<Estimate> Estimates => Set<Estimate>();

    public DbSet<EstimateItem> EstimateItems => Set<EstimateItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyOrganizationQueryFilters(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyPersistenceGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyPersistenceGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyOrganizationQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkshopLocation>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<WorkshopLocation>());

        modelBuilder.Entity<Customer>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<Customer>());

        modelBuilder.Entity<Vehicle>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<Vehicle>());

        modelBuilder.Entity<Appointment>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<Appointment>());

        modelBuilder.Entity<RepairOrder>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<RepairOrder>());

        modelBuilder.Entity<Inspection>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<Inspection>());

        modelBuilder.Entity<InspectionItem>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<InspectionItem>());

        modelBuilder.Entity<Estimate>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<Estimate>());

        modelBuilder.Entity<EstimateItem>()
            .HasQueryFilter(OrganizationFilterName, ApplyOrganizationFilter<EstimateItem>());
    }

    private Expression<Func<TEntity, bool>> ApplyOrganizationFilter<TEntity>()
        where TEntity : OrganizationOwnedEntity
    {
        return entity =>
            _organizationContext.OrganizationId.HasValue
            && entity.OrganizationId == _organizationContext.OrganizationId.Value;
    }

    private void ApplyPersistenceGuards()
    {
        ValidateUtcOffsets();
        ApplyTimestampGuards();
        ApplyTenantWriteGuards();
    }

    private void ValidateUtcOffsets()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                if (!property.Metadata.Name.EndsWith("Utc", StringComparison.Ordinal))
                {
                    continue;
                }

                if (property.CurrentValue is DateTimeOffset dateTimeOffset
                    && dateTimeOffset.Offset != TimeSpan.Zero)
                {
                    throw new InvalidOperationException(
                        $"Property '{entry.Metadata.ClrType.Name}.{property.Metadata.Name}' must use UTC offset zero.");
                }
            }
        }
    }

    private void ApplyTimestampGuards()
    {
        var utcNow = _timeProvider.GetUtcNow();

        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IHasTimestamps.CreatedAtUtc)).CurrentValue = utcNow;
                    entry.Property(nameof(IHasTimestamps.UpdatedAtUtc)).CurrentValue = utcNow;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(IHasTimestamps.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(IHasTimestamps.UpdatedAtUtc)).CurrentValue = utcNow;
                    break;
            }
        }
    }

    private void ApplyTenantWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<IOrganizationOwnedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                case EntityState.Modified:
                case EntityState.Deleted:
                    EnsureOrganizationWriteAllowed(entry);
                    break;
            }
        }
    }

    private void EnsureOrganizationWriteAllowed(EntityEntry entry)
    {
        if (!_organizationContext.IsResolved || !_organizationContext.OrganizationId.HasValue)
        {
            throw new InvalidOperationException(
                "Organization context must be resolved before tenant-owned entities can be written.");
        }

        var currentOrganizationId = _organizationContext.OrganizationId.Value;

        if (entry.Entity is not IOrganizationOwnedEntity organizationOwnedEntity)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            if (organizationOwnedEntity.OrganizationId != currentOrganizationId)
            {
                throw new InvalidOperationException(
                    "Added tenant-owned entities must belong to the resolved organization.");
            }

            return;
        }

        var originalOrganizationId = entry.OriginalValues.GetValue<Guid>(nameof(IOrganizationOwnedEntity.OrganizationId));
        if (originalOrganizationId != currentOrganizationId
            || organizationOwnedEntity.OrganizationId != currentOrganizationId)
        {
            throw new InvalidOperationException(
                "Tenant-owned entities cannot be modified or deleted outside the resolved organization.");
        }

        if (entry.State == EntityState.Modified
            && entry.Property(nameof(IOrganizationOwnedEntity.OrganizationId)).IsModified)
        {
            throw new InvalidOperationException("OrganizationId cannot be changed on tenant-owned entities.");
        }
    }
}
