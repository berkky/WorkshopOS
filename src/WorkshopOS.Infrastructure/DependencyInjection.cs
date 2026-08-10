using Microsoft.Extensions.DependencyInjection;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Application.Customers;
using WorkshopOS.Application.Operations;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Application.Memberships;
using WorkshopOS.Application.Onboarding;
using WorkshopOS.Application.Organizations;
using WorkshopOS.Application.Team;
using WorkshopOS.Infrastructure.Appointments;
using WorkshopOS.Infrastructure.Customers;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.InspectionMedia;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Application.Reporting;
using WorkshopOS.Infrastructure.Billing;
using WorkshopOS.Infrastructure.Catalog;
using WorkshopOS.Infrastructure.CustomerPortal;
using WorkshopOS.Infrastructure.Estimates;
using WorkshopOS.Infrastructure.EstimateSharing;
using WorkshopOS.Infrastructure.Inventory;
using WorkshopOS.Infrastructure.InspectionMedia;
using WorkshopOS.Infrastructure.Inspections;
using WorkshopOS.Infrastructure.RepairOrders;
using WorkshopOS.Infrastructure.Vehicles;
using WorkshopOS.Infrastructure.Memberships;
using WorkshopOS.Infrastructure.Onboarding;
using WorkshopOS.Infrastructure.Operations;
using WorkshopOS.Infrastructure.Organizations;
using WorkshopOS.Infrastructure.Reporting;
using WorkshopOS.Infrastructure.Team;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkshopOsInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<TrustedOrganizationMembershipValidator>();
        services.AddScoped<OrganizationResolutionService>();
        services.AddSingleton<IOrganizationSlugGenerator, OrganizationSlugGenerator>();
        services.AddSingleton<IOwnerOnboardingPostIdentityGate, OwnerOnboardingPostIdentityGate>();
        services.AddScoped<IOwnerOnboardingService, OwnerOnboardingService>();
        services.AddScoped<IOrganizationSelectionService, OrganizationSelectionService>();
        services.AddScoped<ITeamManagementService, TeamManagementService>();
        services.AddScoped<IOrganizationMembershipManagementService, OrganizationMembershipManagementService>();
        services.AddScoped<ICustomerManagementService, CustomerManagementService>();
        services.AddScoped<IVehicleManagementService, VehicleManagementService>();
        services.AddScoped<IAppointmentManagementService, AppointmentManagementService>();
        services.AddSingleton<IRepairOrderNumberGenerator, RepairOrderNumberGenerator>();
        services.AddScoped<IRepairOrderManagementService, RepairOrderManagementService>();
        services.AddScoped<IWorkshopOperationsService, WorkshopOperationsService>();
        services.AddScoped<IInspectionManagementService, InspectionManagementService>();
        services.AddScoped<IInspectionMediaService, InspectionMediaService>();
        services.AddSingleton<IEstimateNumberGenerator, EstimateNumberGenerator>();
        services.AddScoped<IEstimateManagementService, EstimateManagementService>();
        services.AddSingleton<IEstimateShareTokenGenerator, EstimateShareTokenGenerator>();
        services.AddScoped<IEstimateSharingService, EstimateSharingService>();
        services.AddScoped<ICustomerEstimatePortalService, CustomerEstimatePortalService>();
        services.AddScoped<IServiceCatalogService, ServiceCatalogService>();
        services.AddScoped<IPartCatalogService, PartCatalogService>();
        services.AddScoped<IInventoryManagementService, InventoryManagementService>();
        services.AddSingleton<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddScoped<IInvoiceManagementService, InvoiceManagementService>();
        services.AddScoped<IPaymentManagementService, PaymentManagementService>();
        services.AddScoped<IWorkshopReportingService, WorkshopReportingService>();

        return services;
    }
}
