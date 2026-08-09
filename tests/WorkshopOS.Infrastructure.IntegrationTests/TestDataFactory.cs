using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Persistence;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class TestDataFactory
{
    public static Organization CreateOrganization(string suffix) =>
        new(
            $"Organization {suffix}",
            $"org-{suffix}",
            "TRY",
            "Europe/Istanbul");

    public static async Task<Organization> PersistOrganizationAsync(AppDbContext context, string suffix)
    {
        var organization = CreateOrganization(suffix);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        return organization;
    }

    public static async Task<Customer> PersistCustomerAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix)
    {
        var customer = new Customer(organizationId, $"Customer {suffix}");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer;
    }

    public static async Task<Vehicle> PersistVehicleAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        Guid? currentCustomerId = null)
    {
        var vehicle = new Vehicle(organizationId, "Make", "Model", currentCustomerId);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle;
    }

    public static async Task<WorkshopLocation> PersistWorkshopLocationAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix)
    {
        var location = new WorkshopLocation(organizationId, $"Location {suffix}", suffix);
        context.WorkshopLocations.Add(location);
        await context.SaveChangesAsync();
        return location;
    }
}

internal static class DbUpdateExceptionExtensions
{
    public static string? GetPostgresSqlState(this DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException.SqlState;
            }
        }

        return null;
    }
}

internal static class AppDbContextModelExtensions
{
    public static bool HasNamedOrganizationFilter(AppDbContext context, Type entityType)
    {
        var modelEntityType = context.Model.FindEntityType(entityType);
        if (modelEntityType is null)
        {
            return false;
        }

        return modelEntityType.GetDeclaredQueryFilters()
            .Any(filter => string.Equals(filter.Key, AppDbContext.OrganizationFilterName, StringComparison.Ordinal));
    }
}
