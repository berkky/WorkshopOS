using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.Customers;

public static class CustomerManagerPolicy
{
    public static bool CanManageCustomers(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;
}
