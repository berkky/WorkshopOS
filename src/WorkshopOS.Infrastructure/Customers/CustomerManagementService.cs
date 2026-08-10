using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Customers;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Customers;

public sealed class CustomerManagementService : ICustomerManagementService
{
    public const int DefaultPageSize = CustomerListQuery.DefaultPageSize;
    public const int MaxPageSize = CustomerListQuery.MaxPageSize;

    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly TimeProvider _timeProvider;

    public CustomerManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomerListResult> ListCustomersAsync(
        CustomerListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return new CustomerListResult
            {
                Items = Array.Empty<CustomerListItem>(),
                TotalCount = 0,
                Page = 1,
                PageSize = DefaultPageSize,
            };
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => query.PageSize,
        };

        var search = CustomerInputValidator.NormalizeSearch(query.Search);
        var customers = _dbContext.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            customers = customers.Where(customer =>
                EF.Functions.ILike(customer.DisplayName, pattern)
                || (customer.Email != null && EF.Functions.ILike(customer.Email, pattern))
                || (customer.Phone != null && EF.Functions.ILike(customer.Phone, pattern)));
        }

        var totalCount = await customers.CountAsync(cancellationToken);

        var items = await customers
            .OrderBy(customer => customer.DisplayName)
            .ThenBy(customer => customer.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(customer => new CustomerListItem
            {
                CustomerId = customer.Id,
                DisplayName = customer.DisplayName,
                Email = customer.Email,
                Phone = customer.Phone,
                IsActive = customer.IsActive,
            })
            .ToListAsync(cancellationToken);

        return new CustomerListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<CustomerDetails?> GetCustomerDetailsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == customerId, cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var vehicleCount = await _dbContext.Vehicles.AsNoTracking()
            .CountAsync(vehicle => vehicle.CurrentCustomerId == customerId, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var upcomingAppointmentCount = await _dbContext.Appointments.AsNoTracking()
            .CountAsync(
                appointment => appointment.CustomerId == customerId
                               && appointment.ScheduledStartUtc > now
                               && (appointment.Status == Domain.Appointments.AppointmentStatus.Scheduled
                                   || appointment.Status == Domain.Appointments.AppointmentStatus.Confirmed
                                   || appointment.Status == Domain.Appointments.AppointmentStatus.CheckedIn),
                cancellationToken);

        return new CustomerDetails
        {
            CustomerId = customer.Id,
            DisplayName = customer.DisplayName,
            Email = customer.Email,
            Phone = customer.Phone,
            Notes = customer.Notes,
            IsActive = customer.IsActive,
            CreatedAtUtc = customer.CreatedAtUtc,
            UpdatedAtUtc = customer.UpdatedAtUtc,
            VehicleCount = vehicleCount,
            UpcomingAppointmentCount = upcomingAppointmentCount,
        };
    }

    public async Task<CustomerOperationResult<Guid>> CreateCustomerAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return CustomerOperationResult<Guid>.Failed(CustomerOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryValidateCreateInput(command, out var normalized))
        {
            return CustomerOperationResult<Guid>.Failed(CustomerOperationFailureReason.InvalidInput);
        }

        var customer = new Customer(
            organizationId,
            normalized.DisplayName,
            normalized.Email,
            normalized.Phone,
            normalized.Notes);

        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CustomerOperationResult<Guid>.Succeeded(customer.Id);
    }

    public async Task<CustomerOperationResult> UpdateCustomerAsync(
        UpdateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.OrganizationUnresolved);
        }

        if (!TryValidateUpdateInput(command, out var normalized))
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.InvalidInput);
        }

        var customer = await _dbContext.Customers
            .SingleOrDefaultAsync(candidate => candidate.Id == command.CustomerId, cancellationToken);

        if (customer is null)
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.CustomerNotFound);
        }

        customer.UpdateProfile(
            normalized.DisplayName,
            normalized.Email,
            normalized.Phone,
            normalized.Notes,
            normalized.IsActive);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return CustomerOperationResult.Succeeded();
    }

    public async Task<CustomerOperationResult> DeactivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.OrganizationUnresolved);
        }

        var customer = await _dbContext.Customers
            .SingleOrDefaultAsync(candidate => candidate.Id == customerId, cancellationToken);

        if (customer is null)
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.CustomerNotFound);
        }

        customer.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CustomerOperationResult.Succeeded();
    }

    public async Task<CustomerOperationResult> ActivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.OrganizationUnresolved);
        }

        var customer = await _dbContext.Customers
            .SingleOrDefaultAsync(candidate => candidate.Id == customerId, cancellationToken);

        if (customer is null)
        {
            return CustomerOperationResult.Failed(CustomerOperationFailureReason.CustomerNotFound);
        }

        customer.Activate();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CustomerOperationResult.Succeeded();
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

    private static bool TryValidateCreateInput(
        CreateCustomerCommand command,
        out NormalizedCustomerInput normalized)
    {
        var displayName = CustomerInputValidator.NormalizeDisplayName(command.DisplayName);
        var email = CustomerInputValidator.NormalizeEmail(command.Email);
        var phone = CustomerInputValidator.NormalizePhone(command.Phone);
        var notes = CustomerInputValidator.NormalizeNotes(command.Notes);

        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > CustomerInputValidator.MaxDisplayNameLength
            || (email?.Length ?? 0) > CustomerInputValidator.MaxEmailLength
            || (phone?.Length ?? 0) > CustomerInputValidator.MaxPhoneLength
            || (notes?.Length ?? 0) > CustomerInputValidator.MaxNotesLength
            || !CustomerInputValidator.IsValidEmailShape(email))
        {
            normalized = default!;
            return false;
        }

        normalized = new NormalizedCustomerInput(displayName, email, phone, notes, true);
        return true;
    }

    private static bool TryValidateUpdateInput(
        UpdateCustomerCommand command,
        out NormalizedCustomerInput normalized)
    {
        var displayName = CustomerInputValidator.NormalizeDisplayName(command.DisplayName);
        var email = CustomerInputValidator.NormalizeEmail(command.Email);
        var phone = CustomerInputValidator.NormalizePhone(command.Phone);
        var notes = CustomerInputValidator.NormalizeNotes(command.Notes);

        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > CustomerInputValidator.MaxDisplayNameLength
            || (email?.Length ?? 0) > CustomerInputValidator.MaxEmailLength
            || (phone?.Length ?? 0) > CustomerInputValidator.MaxPhoneLength
            || (notes?.Length ?? 0) > CustomerInputValidator.MaxNotesLength
            || !CustomerInputValidator.IsValidEmailShape(email))
        {
            normalized = default!;
            return false;
        }

        normalized = new NormalizedCustomerInput(displayName, email, phone, notes, command.IsActive);
        return true;
    }

    private readonly record struct NormalizedCustomerInput(
        string DisplayName,
        string? Email,
        string? Phone,
        string? Notes,
        bool IsActive);
}
