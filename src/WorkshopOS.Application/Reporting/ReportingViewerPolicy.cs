using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.Reporting;

public static class ReportingViewerPolicy
{
    public static bool CanViewReporting(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;
}
