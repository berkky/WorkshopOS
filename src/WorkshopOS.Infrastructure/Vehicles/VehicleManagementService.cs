using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Vehicles;

public sealed class VehicleManagementService : IVehicleManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public VehicleManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<VehicleListResult> ListVehiclesAsync(
        VehicleListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return new VehicleListResult
            {
                Items = Array.Empty<VehicleListItem>(),
                TotalCount = 0,
                Page = 1,
                PageSize = VehicleListQuery.DefaultPageSize,
            };
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => VehicleListQuery.DefaultPageSize,
            > VehicleListQuery.MaxPageSize => VehicleListQuery.MaxPageSize,
            _ => query.PageSize,
        };

        var search = VehicleInputValidator.NormalizeSearch(query.Search);
        var vehicles = from vehicle in _dbContext.Vehicles.AsNoTracking()
            join customer in _dbContext.Customers.AsNoTracking()
                on vehicle.CurrentCustomerId equals customer.Id into customerJoin
            from customer in customerJoin.DefaultIfEmpty()
            select new { Vehicle = vehicle, Customer = customer };

        if (query.CurrentCustomerId.HasValue)
        {
            var customerId = query.CurrentCustomerId.Value;
            vehicles = vehicles.Where(row => row.Vehicle.CurrentCustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            vehicles = vehicles.Where(row =>
                EF.Functions.ILike(row.Vehicle.Make, pattern)
                || EF.Functions.ILike(row.Vehicle.Model, pattern)
                || (row.Vehicle.Vin != null && EF.Functions.ILike(row.Vehicle.Vin, pattern))
                || (row.Vehicle.RegistrationPlate != null
                    && EF.Functions.ILike(row.Vehicle.RegistrationPlate, pattern))
                || (row.Customer != null && EF.Functions.ILike(row.Customer.DisplayName, pattern)));
        }

        var totalCount = await vehicles.CountAsync(cancellationToken);

        var items = await vehicles
            .OrderBy(row => row.Vehicle.Make)
            .ThenBy(row => row.Vehicle.Model)
            .ThenBy(row => row.Vehicle.ModelYear)
            .ThenBy(row => row.Vehicle.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new VehicleListItem
            {
                VehicleId = row.Vehicle.Id,
                Make = row.Vehicle.Make,
                Model = row.Vehicle.Model,
                ModelYear = row.Vehicle.ModelYear,
                Vin = row.Vehicle.Vin,
                RegistrationPlate = row.Vehicle.RegistrationPlate,
                CurrentCustomerDisplayName = row.Customer != null ? row.Customer.DisplayName : null,
            })
            .ToListAsync(cancellationToken);

        return new VehicleListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<VehicleDetails?> GetVehicleDetailsAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var vehicle = await _dbContext.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == vehicleId, cancellationToken);

        if (vehicle is null)
        {
            return null;
        }

        string? customerDisplayName = null;
        if (vehicle.CurrentCustomerId.HasValue)
        {
            customerDisplayName = await _dbContext.Customers.AsNoTracking()
                .Where(customer => customer.Id == vehicle.CurrentCustomerId.Value)
                .Select(customer => customer.DisplayName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var repairOrderSummary = await _dbContext.RepairOrders.AsNoTracking()
            .Where(repairOrder => repairOrder.VehicleId == vehicleId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                LastOpenedAtUtc = group.Max(repairOrder => repairOrder.OpenedAtUtc),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var upcomingAppointments = await _dbContext.Appointments.AsNoTracking()
            .Where(appointment => appointment.VehicleId == vehicleId
                                  && appointment.ScheduledStartUtc > now
                                  && (appointment.Status == Domain.Appointments.AppointmentStatus.Scheduled
                                      || appointment.Status == Domain.Appointments.AppointmentStatus.Confirmed
                                      || appointment.Status == Domain.Appointments.AppointmentStatus.CheckedIn))
            .OrderBy(appointment => appointment.ScheduledStartUtc)
            .Select(appointment => appointment.ScheduledStartUtc)
            .ToListAsync(cancellationToken);

        return new VehicleDetails
        {
            VehicleId = vehicle.Id,
            Make = vehicle.Make,
            Model = vehicle.Model,
            ModelYear = vehicle.ModelYear,
            Vin = vehicle.Vin,
            RegistrationPlate = vehicle.RegistrationPlate,
            Color = vehicle.Color,
            CurrentCustomerId = vehicle.CurrentCustomerId,
            CurrentCustomerDisplayName = customerDisplayName,
            CreatedAtUtc = vehicle.CreatedAtUtc,
            UpdatedAtUtc = vehicle.UpdatedAtUtc,
            RepairOrderCount = repairOrderSummary?.Count ?? 0,
            LastRepairOrderOpenedAtUtc = repairOrderSummary?.LastOpenedAtUtc,
            UpcomingAppointmentCount = upcomingAppointments.Count,
            NextAppointmentStartUtc = upcomingAppointments.FirstOrDefault(),
        };
    }

    public async Task<VehicleOperationResult<Guid>> CreateVehicleAsync(
        CreateVehicleCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return VehicleOperationResult<Guid>.Failed(VehicleOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryValidateProfileInput(
                command.Make,
                command.Model,
                command.ModelYear,
                command.Vin,
                command.RegistrationPlate,
                command.Color,
                out var normalized))
        {
            return VehicleOperationResult<Guid>.Failed(VehicleOperationFailureReason.InvalidInput);
        }

        if (!await TryValidateCurrentCustomerAsync(command.CurrentCustomerId, cancellationToken))
        {
            return VehicleOperationResult<Guid>.Failed(VehicleOperationFailureReason.CustomerNotFound);
        }

        var vehicle = new Vehicle(
            organizationId,
            normalized.Make,
            normalized.Model,
            command.CurrentCustomerId,
            normalized.Vin,
            normalized.RegistrationPlate,
            normalized.ModelYear,
            normalized.Color);

        _dbContext.Vehicles.Add(vehicle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return VehicleOperationResult<Guid>.Succeeded(vehicle.Id);
    }

    public async Task<VehicleOperationResult> UpdateVehicleAsync(
        UpdateVehicleCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return VehicleOperationResult.Failed(VehicleOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryValidateProfileInput(
                command.Make,
                command.Model,
                command.ModelYear,
                command.Vin,
                command.RegistrationPlate,
                command.Color,
                out var normalized))
        {
            return VehicleOperationResult.Failed(VehicleOperationFailureReason.InvalidInput);
        }

        var vehicle = await _dbContext.Vehicles
            .SingleOrDefaultAsync(candidate => candidate.Id == command.VehicleId, cancellationToken);

        if (vehicle is null)
        {
            return VehicleOperationResult.Failed(VehicleOperationFailureReason.VehicleNotFound);
        }

        vehicle.UpdateProfile(
            normalized.Make,
            normalized.Model,
            normalized.ModelYear,
            normalized.Vin,
            normalized.RegistrationPlate,
            normalized.Color);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return VehicleOperationResult.Succeeded();
    }

    public async Task<VehicleOperationResult> ReassignVehicleCustomerAsync(
        ReassignVehicleCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return VehicleOperationResult.Failed(VehicleOperationFailureReason.OrganizationUnresolved);
        }

        var vehicle = await _dbContext.Vehicles
            .SingleOrDefaultAsync(candidate => candidate.Id == command.VehicleId, cancellationToken);

        if (vehicle is null)
        {
            return VehicleOperationResult.Failed(VehicleOperationFailureReason.VehicleNotFound);
        }

        if (!await TryValidateCurrentCustomerAsync(command.NewCurrentCustomerId, cancellationToken))
        {
            return VehicleOperationResult.Failed(VehicleOperationFailureReason.CustomerNotFound);
        }

        vehicle.ReassignCurrentCustomer(command.NewCurrentCustomerId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return VehicleOperationResult.Succeeded();
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

    private async Task<bool> TryValidateCurrentCustomerAsync(
        Guid? currentCustomerId,
        CancellationToken cancellationToken)
    {
        if (!currentCustomerId.HasValue)
        {
            return true;
        }

        return await _dbContext.Customers.AsNoTracking()
            .AnyAsync(customer => customer.Id == currentCustomerId.Value, cancellationToken);
    }

    private bool TryValidateProfileInput(
        string make,
        string model,
        int? modelYear,
        string? vin,
        string? registrationPlate,
        string? color,
        out NormalizedVehicleInput normalized)
    {
        var normalizedMake = VehicleInputValidator.NormalizeMake(make);
        var normalizedModel = VehicleInputValidator.NormalizeModel(model);
        var normalizedVin = VehicleInputValidator.NormalizeVin(vin);
        var normalizedPlate = VehicleInputValidator.NormalizeRegistrationPlate(registrationPlate);
        var normalizedColor = VehicleInputValidator.NormalizeColor(color);
        var currentYear = _timeProvider.GetUtcNow().Year;

        if (string.IsNullOrWhiteSpace(normalizedMake)
            || string.IsNullOrWhiteSpace(normalizedModel)
            || normalizedMake.Length > VehicleInputValidator.MaxMakeLength
            || normalizedModel.Length > VehicleInputValidator.MaxModelLength
            || (normalizedVin?.Length ?? 0) > VehicleInputValidator.MaxVinLength
            || (normalizedPlate?.Length ?? 0) > VehicleInputValidator.MaxRegistrationPlateLength
            || (normalizedColor?.Length ?? 0) > VehicleInputValidator.MaxColorLength
            || !VehicleInputValidator.IsValidModelYear(modelYear, currentYear))
        {
            normalized = default!;
            return false;
        }

        normalized = new NormalizedVehicleInput(
            normalizedMake,
            normalizedModel,
            modelYear,
            normalizedVin,
            normalizedPlate,
            normalizedColor);
        return true;
    }

    private readonly record struct NormalizedVehicleInput(
        string Make,
        string Model,
        int? ModelYear,
        string? Vin,
        string? RegistrationPlate,
        string? Color);
}
