using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.RepairOrders;

public sealed class RepairOrderManagementService : IRepairOrderManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly IRepairOrderNumberGenerator _numberGenerator;
    private readonly TimeProvider _timeProvider;

    public RepairOrderManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        IRepairOrderNumberGenerator numberGenerator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _numberGenerator = numberGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<RepairOrderListResult> ListRepairOrdersAsync(
        RepairOrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => RepairOrderListQuery.DefaultPageSize,
            > RepairOrderListQuery.MaxPageSize => RepairOrderListQuery.MaxPageSize,
            _ => query.PageSize,
        };

        var repairOrders = BuildRepairOrderProjection();
        repairOrders = ApplyFilters(repairOrders, query);

        var totalCount = await repairOrders.CountAsync(cancellationToken);

        var items = await repairOrders
            .OrderByDescending(row => row.RepairOrder.OpenedAtUtc)
            .ThenBy(row => row.RepairOrder.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new RepairOrderListItem
            {
                RepairOrderId = row.RepairOrder.Id,
                Number = row.RepairOrder.Number,
                Status = row.RepairOrder.Status,
                CustomerDisplayName = row.Customer.DisplayName,
                VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
                WorkshopLocationName = row.Location.Name,
                OpenedAtUtc = row.RepairOrder.OpenedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new RepairOrderListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<RepairOrderDetails?> GetRepairOrderDetailsAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var row = await BuildRepairOrderProjection()
            .SingleOrDefaultAsync(candidate => candidate.RepairOrder.Id == repairOrderId, cancellationToken);

        return row is null ? null : MapDetails(row);
    }

    public async Task<RepairOrderOperationResult<Guid>> CreateRepairOrderAsync(
        CreateRepairOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryNormalizeIntake(command.CustomerConcern, command.InternalNotes, command.Odometer, out var intake))
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.InvalidInput);
        }

        var references = await ResolveIntakeReferencesAsync(
            command.WorkshopLocationId,
            command.CustomerId,
            command.VehicleId,
            cancellationToken);

        if (references is null)
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.WorkshopLocationNotFound);
        }

        if (references.Location is null)
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.WorkshopLocationNotFound);
        }

        if (references.Customer is null)
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.CustomerNotFound);
        }

        if (references.Vehicle is null)
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.VehicleNotFound);
        }

        if (references.CustomerVehicleMismatch)
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.CustomerVehicleMismatch);
        }

        var openedAtUtc = _timeProvider.GetUtcNow();
        var repairOrder = new RepairOrder(
            organizationId,
            command.WorkshopLocationId,
            command.CustomerId,
            command.VehicleId,
            _numberGenerator.Generate(openedAtUtc),
            openedAtUtc,
            customerConcern: intake.CustomerConcern,
            internalNotes: intake.InternalNotes,
            odometer: intake.Odometer);

        _dbContext.RepairOrders.Add(repairOrder);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return RepairOrderOperationResult<Guid>.Succeeded(repairOrder.Id);
    }

    public Task<RepairOrderOperationResult<Guid>> CreateRepairOrderFromAppointmentAsync(
        CreateRepairOrderFromAppointmentCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAppointmentConversionAsync(
            cancellationToken,
            async () =>
            {
                if (!TryGetOrganizationId(out var organizationId))
                {
                    return RepairOrderOperationResult<Guid>.Failed(
                        RepairOrderOperationFailureReason.OrganizationUnresolved);
                }

                if (!TryNormalizeIntake(command.CustomerConcern, command.InternalNotes, command.Odometer, out var intake))
                {
                    return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.InvalidInput);
                }

                var appointment = await _dbContext.Appointments
                    .SingleOrDefaultAsync(candidate => candidate.Id == command.AppointmentId, cancellationToken);

                if (appointment is null)
                {
                    return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.AppointmentNotFound);
                }

                if (await _dbContext.RepairOrders.AnyAsync(
                        candidate => candidate.AppointmentId == appointment.Id,
                        cancellationToken))
                {
                    return RepairOrderOperationResult<Guid>.Failed(
                        RepairOrderOperationFailureReason.DuplicateAppointmentRepairOrder);
                }

                var references = await ResolveIntakeReferencesAsync(
                    appointment.WorkshopLocationId,
                    appointment.CustomerId,
                    appointment.VehicleId,
                    cancellationToken);

                if (references is null || references.Location is null)
                {
                    return RepairOrderOperationResult<Guid>.Failed(
                        RepairOrderOperationFailureReason.WorkshopLocationNotFound);
                }

                if (references.Customer is null)
                {
                    return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.CustomerNotFound);
                }

                if (references.Vehicle is null)
                {
                    return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.VehicleNotFound);
                }

                if (references.CustomerVehicleMismatch)
                {
                    return RepairOrderOperationResult<Guid>.Failed(
                        RepairOrderOperationFailureReason.CustomerVehicleMismatch);
                }

                var openedAtUtc = _timeProvider.GetUtcNow();
                var customerConcern = intake.CustomerConcern ?? appointment.CustomerConcern;
                var internalNotes = intake.InternalNotes ?? appointment.InternalNotes;

                var repairOrder = new RepairOrder(
                    organizationId,
                    appointment.WorkshopLocationId,
                    appointment.CustomerId,
                    appointment.VehicleId,
                    _numberGenerator.Generate(openedAtUtc),
                    openedAtUtc,
                    appointmentId: appointment.Id,
                    customerConcern: customerConcern,
                    internalNotes: internalNotes,
                    odometer: intake.Odometer);

                _dbContext.RepairOrders.Add(repairOrder);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return RepairOrderOperationResult<Guid>.Succeeded(repairOrder.Id);
            });

    public async Task<RepairOrderOperationResult> UpdateRepairOrderIntakeAsync(
        UpdateRepairOrderIntakeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryNormalizeIntake(command.CustomerConcern, command.InternalNotes, command.Odometer, out var intake))
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.InvalidInput);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RepairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.RepairOrderNotFound);
        }

        if (RepairOrderLifecyclePolicy.IsTerminal(repairOrder.Status))
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.InvalidLifecycleTransition);
        }

        repairOrder.UpdateIntake(intake.CustomerConcern, intake.InternalNotes, intake.Odometer);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RepairOrderOperationResult.Succeeded();
    }

    public async Task<RepairOrderOperationResult> StartRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default) =>
        await ExecuteLifecycleAsync(repairOrderId, cancellationToken, repairOrder =>
        {
            if (!RepairOrderLifecyclePolicy.CanStartWork(repairOrder.Status))
            {
                return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.InvalidLifecycleTransition);
            }

            repairOrder.StartWork();
            return RepairOrderOperationResult.Succeeded();
        });

    public async Task<RepairOrderOperationResult> CompleteRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default) =>
        await ExecuteLifecycleAsync(repairOrderId, cancellationToken, repairOrder =>
        {
            if (!RepairOrderLifecyclePolicy.CanComplete(repairOrder.Status))
            {
                return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.InvalidLifecycleTransition);
            }

            repairOrder.Complete(_timeProvider.GetUtcNow());
            return RepairOrderOperationResult.Succeeded();
        });

    public async Task<RepairOrderOperationResult> CancelRepairOrderAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken = default) =>
        await ExecuteLifecycleAsync(repairOrderId, cancellationToken, repairOrder =>
        {
            if (!RepairOrderLifecyclePolicy.CanCancel(repairOrder.Status))
            {
                return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.InvalidLifecycleTransition);
            }

            repairOrder.Cancel();
            return RepairOrderOperationResult.Succeeded();
        });

    private async Task<RepairOrderOperationResult> ExecuteLifecycleAsync(
        Guid repairOrderId,
        CancellationToken cancellationToken,
        Func<RepairOrder, RepairOrderOperationResult> mutate)
    {
        if (!TryGetOrganizationId(out _))
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.OrganizationUnresolved);
        }

        var repairOrder = await _dbContext.RepairOrders
            .SingleOrDefaultAsync(candidate => candidate.Id == repairOrderId, cancellationToken);

        if (repairOrder is null)
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.RepairOrderNotFound);
        }

        try
        {
            var result = mutate(repairOrder);
            if (!result.Success)
            {
                return result;
            }
        }
        catch (InvalidOperationException)
        {
            return RepairOrderOperationResult.Failed(RepairOrderOperationFailureReason.InvalidLifecycleTransition);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return RepairOrderOperationResult.Succeeded();
    }

    private async Task<RepairOrderOperationResult<Guid>> ExecuteAppointmentConversionAsync(
        CancellationToken cancellationToken,
        Func<Task<RepairOrderOperationResult<Guid>>> operation)
    {
        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        }

        try
        {
            var result = await operation();
            if (ownsTransaction && result.Success)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return RepairOrderOperationResult<Guid>.Failed(RepairOrderOperationFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<IntakeReferences?> ResolveIntakeReferencesAsync(
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var location = await _dbContext.WorkshopLocations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == workshopLocationId, cancellationToken);

        if (location is null || !location.IsActive)
        {
            return new IntakeReferences(null, null, null, false);
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == customerId, cancellationToken);

        var vehicle = await _dbContext.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == vehicleId, cancellationToken);

        var customerVehicleMismatch = vehicle?.CurrentCustomerId is Guid currentCustomerId
                                      && currentCustomerId != customerId;

        return new IntakeReferences(location, customer, vehicle, customerVehicleMismatch);
    }

    private IQueryable<RepairOrderProjectionRow> BuildRepairOrderProjection() =>
        from repairOrder in _dbContext.RepairOrders.AsNoTracking()
        join location in _dbContext.WorkshopLocations.AsNoTracking()
            on repairOrder.WorkshopLocationId equals location.Id
        join customer in _dbContext.Customers.AsNoTracking()
            on repairOrder.CustomerId equals customer.Id
        join vehicle in _dbContext.Vehicles.AsNoTracking()
            on repairOrder.VehicleId equals vehicle.Id
        select new RepairOrderProjectionRow
        {
            RepairOrder = repairOrder,
            Location = location,
            Customer = customer,
            Vehicle = vehicle,
        };

    private static IQueryable<RepairOrderProjectionRow> ApplyFilters(
        IQueryable<RepairOrderProjectionRow> repairOrders,
        RepairOrderListQuery query)
    {
        var search = RepairOrderInputValidator.NormalizeSearch(query.Search);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            repairOrders = repairOrders.Where(row =>
                EF.Functions.ILike(row.RepairOrder.Number, pattern)
                || EF.Functions.ILike(row.Customer.DisplayName, pattern)
                || EF.Functions.ILike(row.Vehicle.Make, pattern)
                || EF.Functions.ILike(row.Vehicle.Model, pattern)
                || (row.Vehicle.RegistrationPlate != null
                    && EF.Functions.ILike(row.Vehicle.RegistrationPlate, pattern)));
        }

        if (query.Status.HasValue)
        {
            repairOrders = repairOrders.Where(row => row.RepairOrder.Status == query.Status.Value);
        }

        if (query.WorkshopLocationId.HasValue)
        {
            repairOrders = repairOrders.Where(row =>
                row.RepairOrder.WorkshopLocationId == query.WorkshopLocationId.Value);
        }

        if (query.CustomerId.HasValue)
        {
            repairOrders = repairOrders.Where(row => row.RepairOrder.CustomerId == query.CustomerId.Value);
        }

        if (query.VehicleId.HasValue)
        {
            repairOrders = repairOrders.Where(row => row.RepairOrder.VehicleId == query.VehicleId.Value);
        }

        return repairOrders;
    }

    private static RepairOrderDetails MapDetails(RepairOrderProjectionRow row) =>
        new()
        {
            RepairOrderId = row.RepairOrder.Id,
            Number = row.RepairOrder.Number,
            Status = row.RepairOrder.Status,
            WorkshopLocationId = row.RepairOrder.WorkshopLocationId,
            WorkshopLocationName = row.Location.Name,
            AppointmentId = row.RepairOrder.AppointmentId,
            CustomerId = row.RepairOrder.CustomerId,
            CustomerDisplayName = row.Customer.DisplayName,
            VehicleId = row.RepairOrder.VehicleId,
            VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            CustomerConcern = row.RepairOrder.CustomerConcern,
            InternalNotes = row.RepairOrder.InternalNotes,
            Odometer = row.RepairOrder.Odometer,
            OpenedAtUtc = row.RepairOrder.OpenedAtUtc,
            CompletedAtUtc = row.RepairOrder.CompletedAtUtc,
            CreatedAtUtc = row.RepairOrder.CreatedAtUtc,
            UpdatedAtUtc = row.RepairOrder.UpdatedAtUtc,
        };

    private static bool IsSerializationConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException && postgresException.SqlState is "40001")
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool TryNormalizeIntake(
        string? customerConcern,
        string? internalNotes,
        int? odometer,
        out NormalizedIntake intake)
    {
        var concern = RepairOrderInputValidator.NormalizeCustomerConcern(customerConcern);
        var notes = RepairOrderInputValidator.NormalizeInternalNotes(internalNotes);

        if ((concern?.Length ?? 0) > RepairOrderInputValidator.MaxCustomerConcernLength
            || (notes?.Length ?? 0) > RepairOrderInputValidator.MaxInternalNotesLength
            || !RepairOrderInputValidator.IsValidOdometer(odometer))
        {
            intake = default!;
            return false;
        }

        intake = new NormalizedIntake(concern, notes, odometer);
        return true;
    }

    private static RepairOrderListResult EmptyListResult() =>
        new()
        {
            Items = Array.Empty<RepairOrderListItem>(),
            TotalCount = 0,
            Page = 1,
            PageSize = RepairOrderListQuery.DefaultPageSize,
        };

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private sealed class RepairOrderProjectionRow
    {
        public required RepairOrder RepairOrder { get; init; }

        public required Domain.Organizations.WorkshopLocation Location { get; init; }

        public required Domain.Customers.Customer Customer { get; init; }

        public required Domain.Vehicles.Vehicle Vehicle { get; init; }
    }

    private sealed record IntakeReferences(
        Domain.Organizations.WorkshopLocation? Location,
        Domain.Customers.Customer? Customer,
        Domain.Vehicles.Vehicle? Vehicle,
        bool CustomerVehicleMismatch);

    private readonly record struct NormalizedIntake(
        string? CustomerConcern,
        string? InternalNotes,
        int? Odometer);
}
