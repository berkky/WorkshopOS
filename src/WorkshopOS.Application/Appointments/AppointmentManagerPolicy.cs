using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.Appointments;

public static class AppointmentManagerPolicy
{
    public static bool CanManageAppointments(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;
}
