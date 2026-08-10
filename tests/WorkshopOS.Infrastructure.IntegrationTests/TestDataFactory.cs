using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Customers;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Domain.Vehicles;
using WorkshopOS.Infrastructure.Identity;
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
        Guid? currentCustomerId = null,
        string make = "Make",
        string model = "Model",
        string? vin = null,
        string? registrationPlate = null)
    {
        var vehicle = new Vehicle(
            organizationId,
            make,
            model,
            currentCustomerId,
            vin,
            registrationPlate);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle;
    }

    public static async Task<Appointment> PersistAppointmentAsync(
        AppDbContext context,
        Guid organizationId,
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        AppointmentStatus status = AppointmentStatus.Scheduled)
    {
        var appointment = new Appointment(
            organizationId,
            workshopLocationId,
            customerId,
            vehicleId,
            startUtc,
            endUtc,
            status);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment;
    }

    public static async Task<RepairOrder> PersistRepairOrderAsync(
        AppDbContext context,
        Guid organizationId,
        Guid workshopLocationId,
        Guid customerId,
        Guid vehicleId,
        string suffix,
        DateTimeOffset openedAtUtc,
        RepairOrderStatus status = RepairOrderStatus.Draft,
        RepairOrderPriority priority = RepairOrderPriority.Normal)
    {
        var repairOrder = new RepairOrder(
            organizationId,
            workshopLocationId,
            customerId,
            vehicleId,
            $"RO-{suffix}",
            openedAtUtc,
            status,
            priority);
        context.RepairOrders.Add(repairOrder);
        await context.SaveChangesAsync();
        return repairOrder;
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

    public static async Task<ApplicationUser> PersistUserAsync(AppDbContext context, string suffix)
    {
        var userManager = IdentityTestServiceFactory.CreateUserManager(context);
        var user = new ApplicationUser
        {
            UserName = $"user-{suffix}@example.com",
            Email = $"user-{suffix}@example.com",
            EmailConfirmed = true,
            DisplayName = $"User {suffix}"
        };

        var result = await userManager.CreateAsync(user, IdentityTestServiceFactory.ValidTestPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Failed to create test user.");
        }

        return user;
    }

    public static async Task<OrganizationMembership> PersistMembershipAsync(
        AppDbContext context,
        Guid organizationId,
        Guid userId,
        OrganizationMembershipRole role,
        OrganizationMembershipStatus status = OrganizationMembershipStatus.Active)
    {
        var membership = new OrganizationMembership(organizationId, userId, role, status);
        context.OrganizationMemberships.Add(membership);
        await context.SaveChangesAsync();
        return membership;
    }

    public static async Task<StaffMember> PersistStaffMemberAsync(
        AppDbContext context,
        Guid organizationId,
        string suffix,
        Guid? userId = null,
        StaffPosition position = StaffPosition.Technician,
        StaffStatus status = StaffStatus.Active)
    {
        var staffMember = new StaffMember(
            organizationId,
            $"Staff {suffix}",
            position,
            userId: userId,
            status: status);
        context.StaffMembers.Add(staffMember);
        await context.SaveChangesAsync();
        return staffMember;
    }

    public static async Task<StaffLocationAssignment> PersistStaffLocationAssignmentAsync(
        AppDbContext context,
        Guid organizationId,
        Guid staffMemberId,
        Guid workshopLocationId)
    {
        var assignment = new StaffLocationAssignment(organizationId, staffMemberId, workshopLocationId);
        context.StaffLocationAssignments.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    public static async Task<(StaffMember StaffMember, StaffLocationAssignment LocationAssignment)> PersistTechnicianAtLocationAsync(
        AppDbContext context,
        Guid organizationId,
        Guid workshopLocationId,
        string suffix,
        Guid? userId = null,
        StaffStatus status = StaffStatus.Active)
    {
        var staffMember = await PersistStaffMemberAsync(
            context,
            organizationId,
            suffix,
            userId,
            StaffPosition.Technician,
            status);
        var locationAssignment = await PersistStaffLocationAssignmentAsync(
            context,
            organizationId,
            staffMember.Id,
            workshopLocationId);
        return (staffMember, locationAssignment);
    }

    public static async Task<RepairOrderTechnicianAssignment> PersistRepairOrderTechnicianAssignmentAsync(
        AppDbContext context,
        Guid organizationId,
        Guid repairOrderId,
        Guid staffMemberId,
        DateTimeOffset assignedAtUtc,
        DateTimeOffset? unassignedAtUtc = null)
    {
        var assignment = new RepairOrderTechnicianAssignment(
            organizationId,
            repairOrderId,
            staffMemberId,
            assignedAtUtc);

        if (unassignedAtUtc.HasValue)
        {
            assignment.Unassign(unassignedAtUtc.Value);
        }

        context.RepairOrderTechnicianAssignments.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
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
