using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.Vehicles;

public static class VehicleManagerPolicy
{
    public static bool CanManageVehicles(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;
}
