using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Appointments;

public sealed class AppointmentManagementService : IAppointmentManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public AppointmentManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentListResult> ListAppointmentsAsync(
        AppointmentListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EmptyListResult();
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => AppointmentListQuery.DefaultPageSize,
            > AppointmentListQuery.MaxPageSize => AppointmentListQuery.MaxPageSize,
            _ => query.PageSize,
        };

        if (query.FromUtc.HasValue
            && query.ToUtc.HasValue
            && !AppointmentInputValidator.IsWithinCalendarRange(query.FromUtc.Value, query.ToUtc.Value))
        {
            return EmptyListResult(page, pageSize);
        }

        var appointments = BuildAppointmentProjection();

        appointments = ApplyFilters(appointments, query.WorkshopLocationId, query.CustomerId, query.VehicleId, query.Status);

        if (query.FromUtc.HasValue)
        {
            appointments = appointments.Where(row => row.Appointment.ScheduledEndUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            appointments = appointments.Where(row => row.Appointment.ScheduledStartUtc < query.ToUtc.Value);
        }

        var totalCount = await appointments.CountAsync(cancellationToken);

        var items = await appointments
            .OrderBy(row => row.Appointment.ScheduledStartUtc)
            .ThenBy(row => row.Appointment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new AppointmentListItem
            {
                AppointmentId = row.Appointment.Id,
                WorkshopLocationId = row.Appointment.WorkshopLocationId,
                ScheduledStartUtc = row.Appointment.ScheduledStartUtc,
                ScheduledEndUtc = row.Appointment.ScheduledEndUtc,
                Status = row.Appointment.Status,
                WorkshopLocationName = row.Location.Name,
                CustomerDisplayName = row.Customer.DisplayName,
                VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            })
            .ToListAsync(cancellationToken);

        return new AppointmentListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<AppointmentCalendarResult> GetCalendarAppointmentsAsync(
        AppointmentCalendarQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return new AppointmentCalendarResult
            {
                WeekStartLocal = query.WeekStartLocal,
                TimeZoneId = "UTC",
                Items = Array.Empty<AppointmentCalendarItem>(),
            };
        }

        var organization = await _dbContext.Organizations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == organizationId, cancellationToken);

        string timeZoneId = organization.TimeZoneId;
        if (query.WorkshopLocationId.HasValue)
        {
            var locationTimeZone = await _dbContext.WorkshopLocations.AsNoTracking()
                .Where(location => location.Id == query.WorkshopLocationId.Value)
                .Select(location => location.TimeZoneId)
                .SingleOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(locationTimeZone))
            {
                timeZoneId = locationTimeZone;
            }
        }

        var weekStartLocal = AppointmentSchedulingConverter.GetWeekStartMonday(query.WeekStartLocal);
        var weekStartDateTime = weekStartLocal.ToDateTime(TimeOnly.MinValue);
        var weekEndDateTime = weekStartLocal.AddDays(7).ToDateTime(TimeOnly.MinValue);

        if (!AppointmentSchedulingConverter.TryConvertLocalToUtc(timeZoneId, weekStartDateTime, out var rangeStartUtc, out _)
            || !AppointmentSchedulingConverter.TryConvertLocalToUtc(timeZoneId, weekEndDateTime, out var rangeEndUtc, out _))
        {
            return new AppointmentCalendarResult
            {
                WeekStartLocal = weekStartLocal,
                TimeZoneId = timeZoneId,
                Items = Array.Empty<AppointmentCalendarItem>(),
            };
        }

        if (!AppointmentInputValidator.IsWithinCalendarRange(rangeStartUtc, rangeEndUtc))
        {
            return new AppointmentCalendarResult
            {
                WeekStartLocal = weekStartLocal,
                TimeZoneId = timeZoneId,
                Items = Array.Empty<AppointmentCalendarItem>(),
            };
        }

        var appointments = BuildAppointmentProjection();
        appointments = ApplyFilters(appointments, query.WorkshopLocationId, null, null, query.Status);
        appointments = appointments.Where(row =>
            row.Appointment.ScheduledStartUtc < rangeEndUtc
            && row.Appointment.ScheduledEndUtc > rangeStartUtc);

        var rows = await appointments
            .OrderBy(row => row.Appointment.ScheduledStartUtc)
            .ThenBy(row => row.Appointment.Id)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AppointmentCalendarItem
            {
                AppointmentId = row.Appointment.Id,
                ScheduledStartUtc = row.Appointment.ScheduledStartUtc,
                ScheduledEndUtc = row.Appointment.ScheduledEndUtc,
                ScheduledStartLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(
                    row.Appointment.ScheduledStartUtc,
                    timeZoneId),
                ScheduledEndLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(
                    row.Appointment.ScheduledEndUtc,
                    timeZoneId),
                Status = row.Appointment.Status,
                WorkshopLocationName = row.Location.Name,
                CustomerDisplayName = row.Customer.DisplayName,
                VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            })
            .ToList();

        return new AppointmentCalendarResult
        {
            WeekStartLocal = weekStartLocal,
            TimeZoneId = timeZoneId,
            Items = items,
        };
    }

    public async Task<AppointmentDetails?> GetAppointmentDetailsAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return null;
        }

        var row = await BuildAppointmentProjection()
            .SingleOrDefaultAsync(candidate => candidate.Appointment.Id == appointmentId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var organization = await _dbContext.Organizations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == organizationId, cancellationToken);
        var timeZoneId = row.Location.TimeZoneId ?? organization.TimeZoneId;

        var linkedRepairOrderId = await _dbContext.RepairOrders.AsNoTracking()
            .Where(candidate => candidate.AppointmentId == appointmentId)
            .Select(candidate => candidate.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return new AppointmentDetails
        {
            AppointmentId = row.Appointment.Id,
            WorkshopLocationId = row.Appointment.WorkshopLocationId,
            WorkshopLocationName = row.Location.Name,
            CustomerId = row.Appointment.CustomerId,
            CustomerDisplayName = row.Customer.DisplayName,
            VehicleId = row.Appointment.VehicleId,
            VehicleSummary = FormatVehicleSummary(row.Vehicle.Make, row.Vehicle.Model, row.Vehicle.ModelYear),
            Status = row.Appointment.Status,
            ScheduledStartUtc = row.Appointment.ScheduledStartUtc,
            ScheduledEndUtc = row.Appointment.ScheduledEndUtc,
            TimeZoneId = timeZoneId,
            CustomerConcern = row.Appointment.CustomerConcern,
            InternalNotes = row.Appointment.InternalNotes,
            CreatedAtUtc = row.Appointment.CreatedAtUtc,
            UpdatedAtUtc = row.Appointment.UpdatedAtUtc,
            LinkedRepairOrderId = linkedRepairOrderId == Guid.Empty ? null : linkedRepairOrderId,
        };
    }

    public Task<AppointmentOperationResult<Guid>> CreateAppointmentAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteOverlapSensitiveAsync(
            cancellationToken,
            async () =>
            {
                if (!TryGetOrganizationId(out var organizationId))
                {
                    return AppointmentOperationResult<Guid>.Failed(
                        AppointmentOperationFailureReason.OrganizationUnresolved);
                }

                if (!TryNormalizeNotes(command.CustomerConcern, command.InternalNotes, out var notes))
                {
                    return AppointmentOperationResult<Guid>.Failed(AppointmentOperationFailureReason.InvalidInput);
                }

                if (!AppointmentInputValidator.IsValidTimeRange(
                        command.ScheduledStartUtc,
                        command.ScheduledEndUtc))
                {
                    return AppointmentOperationResult<Guid>.Failed(AppointmentOperationFailureReason.InvalidInput);
                }

                if (command.ScheduledStartUtc < _timeProvider.GetUtcNow())
                {
                    return AppointmentOperationResult<Guid>.Failed(
                        AppointmentOperationFailureReason.PastStartNotAllowed);
                }

                var references = await ResolveBookingReferencesAsync(
                    command.WorkshopLocationId,
                    command.CustomerId,
                    command.VehicleId,
                    cancellationToken);

                if (references is null)
                {
                    return AppointmentOperationResult<Guid>.Failed(
                        AppointmentOperationFailureReason.WorkshopLocationNotFound);
                }

                if (references.Location is null)
                {
                    return AppointmentOperationResult<Guid>.Failed(
                        AppointmentOperationFailureReason.WorkshopLocationNotFound);
                }

                if (references.Customer is null)
                {
                    return AppointmentOperationResult<Guid>.Failed(AppointmentOperationFailureReason.CustomerNotFound);
                }

                if (references.Vehicle is null)
                {
                    return AppointmentOperationResult<Guid>.Failed(AppointmentOperationFailureReason.VehicleNotFound);
                }

                if (references.CustomerVehicleMismatch)
                {
                    return AppointmentOperationResult<Guid>.Failed(
                        AppointmentOperationFailureReason.CustomerVehicleMismatch);
                }

                if (await HasVehicleOverlapAsync(
                        command.VehicleId,
                        command.ScheduledStartUtc,
                        command.ScheduledEndUtc,
                        excludeAppointmentId: null,
                        cancellationToken))
                {
                    return AppointmentOperationResult<Guid>.Failed(AppointmentOperationFailureReason.VehicleOverlap);
                }

                var appointment = new Appointment(
                    organizationId,
                    command.WorkshopLocationId,
                    command.CustomerId,
                    command.VehicleId,
                    command.ScheduledStartUtc,
                    command.ScheduledEndUtc,
                    customerConcern: notes.CustomerConcern,
                    internalNotes: notes.InternalNotes);

                _dbContext.Appointments.Add(appointment);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return AppointmentOperationResult<Guid>.Succeeded(appointment.Id);
            });

    public async Task<AppointmentOperationResult> UpdateAppointmentAsync(
        UpdateAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryNormalizeNotes(command.CustomerConcern, command.InternalNotes, out var notes))
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.InvalidInput);
        }

        var appointment = await _dbContext.Appointments
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.AppointmentNotFound);
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.CannotModifyCancelled);
        }

        var references = await ResolveBookingReferencesAsync(
            command.WorkshopLocationId,
            command.CustomerId,
            command.VehicleId,
            cancellationToken);

        if (references is null)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.WorkshopLocationNotFound);
        }

        if (references.Location is null)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.WorkshopLocationNotFound);
        }

        if (references.Customer is null)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.CustomerNotFound);
        }

        if (references.Vehicle is null)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.VehicleNotFound);
        }

        if (references.CustomerVehicleMismatch)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.CustomerVehicleMismatch);
        }

        appointment.UpdateBooking(
            command.WorkshopLocationId,
            command.CustomerId,
            command.VehicleId,
            notes.CustomerConcern,
            notes.InternalNotes);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return AppointmentOperationResult.Succeeded();
    }

    public Task<AppointmentOperationResult> RescheduleAppointmentAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteOverlapSensitiveAsync(
            cancellationToken,
            async () =>
            {
                if (!TryGetOrganizationId(out _))
                {
                    return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.OrganizationUnresolved);
                }

                if (!AppointmentInputValidator.IsValidTimeRange(
                        command.ScheduledStartUtc,
                        command.ScheduledEndUtc))
                {
                    return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.InvalidInput);
                }

                var appointment = await _dbContext.Appointments
                    .SingleOrDefaultAsync(candidate => candidate.Id == command.AppointmentId, cancellationToken);

                if (appointment is null)
                {
                    return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.AppointmentNotFound);
                }

                if (appointment.Status == AppointmentStatus.Cancelled)
                {
                    return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.CannotModifyCancelled);
                }

                var references = await ResolveBookingReferencesAsync(
                    command.WorkshopLocationId,
                    appointment.CustomerId,
                    appointment.VehicleId,
                    cancellationToken);

                if (references is null || references.Location is null)
                {
                    return AppointmentOperationResult.Failed(
                        AppointmentOperationFailureReason.WorkshopLocationNotFound);
                }

                if (await HasVehicleOverlapAsync(
                        appointment.VehicleId,
                        command.ScheduledStartUtc,
                        command.ScheduledEndUtc,
                        appointment.Id,
                        cancellationToken))
                {
                    return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.VehicleOverlap);
                }

                appointment.Reschedule(
                    command.WorkshopLocationId,
                    command.ScheduledStartUtc,
                    command.ScheduledEndUtc);

                await _dbContext.SaveChangesAsync(cancellationToken);
                return AppointmentOperationResult.Succeeded();
            });

    public async Task<AppointmentOperationResult> CancelAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.OrganizationUnresolved);
        }

        var appointment = await _dbContext.Appointments
            .SingleOrDefaultAsync(candidate => candidate.Id == appointmentId, cancellationToken);

        if (appointment is null)
        {
            return AppointmentOperationResult.Failed(AppointmentOperationFailureReason.AppointmentNotFound);
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return AppointmentOperationResult.Succeeded();
        }

        appointment.Cancel();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return AppointmentOperationResult.Succeeded();
    }

    public async Task<string?> ResolveSchedulingTimeZoneAsync(
        Guid workshopLocationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return null;
        }

        var location = await _dbContext.WorkshopLocations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == workshopLocationId, cancellationToken);

        if (location is null || !location.IsActive)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(location.TimeZoneId))
        {
            return location.TimeZoneId;
        }

        return await _dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.TimeZoneId)
            .SingleAsync(cancellationToken);
    }

    private async Task<AppointmentOperationResult<T>> ExecuteOverlapSensitiveAsync<T>(
        CancellationToken cancellationToken,
        Func<Task<AppointmentOperationResult<T>>> operation)
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
            return AppointmentOperationResult<T>.Failed(AppointmentOperationFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<AppointmentOperationResult> ExecuteOverlapSensitiveAsync(
        CancellationToken cancellationToken,
        Func<Task<AppointmentOperationResult>> operation)
    {
        var result = await ExecuteOverlapSensitiveAsync(
            cancellationToken,
            async () =>
            {
                var innerResult = await operation();
                return innerResult.Success
                    ? AppointmentOperationResult<bool>.Succeeded(true)
                    : AppointmentOperationResult<bool>.Failed(innerResult.FailureReason!.Value);
            });

        return result.Success
            ? AppointmentOperationResult.Succeeded()
            : AppointmentOperationResult.Failed(result.FailureReason!.Value);
    }

    private async Task<bool> HasVehicleOverlapAsync(
        Guid vehicleId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        var blockingStatuses = GetBlockingStatuses();

        return await _dbContext.Appointments
            .Where(appointment =>
                appointment.VehicleId == vehicleId
                && blockingStatuses.Contains(appointment.Status)
                && appointment.ScheduledStartUtc < endUtc
                && appointment.ScheduledEndUtc > startUtc
                && (!excludeAppointmentId.HasValue || appointment.Id != excludeAppointmentId.Value))
            .AnyAsync(cancellationToken);
    }

    private async Task<BookingReferences?> ResolveBookingReferencesAsync(
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
            return new BookingReferences(location, null, null, false);
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == customerId, cancellationToken);

        var vehicle = await _dbContext.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == vehicleId, cancellationToken);

        var customerVehicleMismatch = vehicle?.CurrentCustomerId is Guid currentCustomerId
                                      && currentCustomerId != customerId;

        return new BookingReferences(location, customer, vehicle, customerVehicleMismatch);
    }

    private IQueryable<AppointmentProjectionRow> BuildAppointmentProjection() =>
        from appointment in _dbContext.Appointments.AsNoTracking()
        join location in _dbContext.WorkshopLocations.AsNoTracking()
            on appointment.WorkshopLocationId equals location.Id
        join customer in _dbContext.Customers.AsNoTracking()
            on appointment.CustomerId equals customer.Id
        join vehicle in _dbContext.Vehicles.AsNoTracking()
            on appointment.VehicleId equals vehicle.Id
        select new AppointmentProjectionRow
        {
            Appointment = appointment,
            Location = location,
            Customer = customer,
            Vehicle = vehicle,
        };

    private static IQueryable<AppointmentProjectionRow> ApplyFilters(
        IQueryable<AppointmentProjectionRow> appointments,
        Guid? workshopLocationId,
        Guid? customerId,
        Guid? vehicleId,
        AppointmentStatus? status)
    {
        if (workshopLocationId.HasValue)
        {
            appointments = appointments.Where(row => row.Appointment.WorkshopLocationId == workshopLocationId.Value);
        }

        if (customerId.HasValue)
        {
            appointments = appointments.Where(row => row.Appointment.CustomerId == customerId.Value);
        }

        if (vehicleId.HasValue)
        {
            appointments = appointments.Where(row => row.Appointment.VehicleId == vehicleId.Value);
        }

        if (status.HasValue)
        {
            appointments = appointments.Where(row => row.Appointment.Status == status.Value);
        }

        return appointments;
    }

    private static AppointmentStatus[] GetBlockingStatuses() =>
    [
        AppointmentStatus.Scheduled,
        AppointmentStatus.Confirmed,
        AppointmentStatus.CheckedIn,
    ];

    private static bool IsSerializationConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState is "40001")
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

    private static bool TryNormalizeNotes(
        string? customerConcern,
        string? internalNotes,
        out NormalizedNotes normalized)
    {
        var concern = AppointmentInputValidator.NormalizeCustomerConcern(customerConcern);
        var notes = AppointmentInputValidator.NormalizeInternalNotes(internalNotes);

        if ((concern?.Length ?? 0) > AppointmentInputValidator.MaxCustomerConcernLength
            || (notes?.Length ?? 0) > AppointmentInputValidator.MaxInternalNotesLength)
        {
            normalized = default!;
            return false;
        }

        normalized = new NormalizedNotes(concern, notes);
        return true;
    }

    private static AppointmentListResult EmptyListResult(int page = 1, int pageSize = AppointmentListQuery.DefaultPageSize) =>
        new()
        {
            Items = Array.Empty<AppointmentListItem>(),
            TotalCount = 0,
            Page = page,
            PageSize = pageSize,
        };

    private static string FormatVehicleSummary(string make, string model, int? modelYear) =>
        modelYear.HasValue ? $"{make} {model} ({modelYear})" : $"{make} {model}";

    private sealed class AppointmentProjectionRow
    {
        public required Appointment Appointment { get; init; }

        public required Domain.Organizations.WorkshopLocation Location { get; init; }

        public required Domain.Customers.Customer Customer { get; init; }

        public required Domain.Vehicles.Vehicle Vehicle { get; init; }
    }

    private sealed record BookingReferences(
        Domain.Organizations.WorkshopLocation? Location,
        Domain.Customers.Customer? Customer,
        Domain.Vehicles.Vehicle? Vehicle,
        bool CustomerVehicleMismatch);

    private readonly record struct NormalizedNotes(string? CustomerConcern, string? InternalNotes);
}
