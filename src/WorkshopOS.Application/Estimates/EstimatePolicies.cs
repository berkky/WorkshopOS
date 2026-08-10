namespace WorkshopOS.Application.Estimates;

public static class EstimateManagerPolicy
{
    public static bool CanManageEstimates(Domain.Organizations.OrganizationMembershipRole role) =>
        role is Domain.Organizations.OrganizationMembershipRole.Owner
            or Domain.Organizations.OrganizationMembershipRole.Administrator
            or Domain.Organizations.OrganizationMembershipRole.ServiceAdvisor;
}

public static class EstimateLifecyclePolicy
{
    public static bool IsFinanciallyMutable(Domain.Estimates.EstimateStatus status) =>
        status is Domain.Estimates.EstimateStatus.Draft;

    public static bool CanPresent(Domain.Estimates.EstimateStatus status) =>
        status is Domain.Estimates.EstimateStatus.Draft;

    public static bool CanRecordCustomerDecision(Domain.Estimates.EstimateStatus status) =>
        status is Domain.Estimates.EstimateStatus.Sent;

    public static bool IsTerminal(Domain.Estimates.EstimateStatus status) =>
        status is Domain.Estimates.EstimateStatus.Approved
            or Domain.Estimates.EstimateStatus.Declined;
}
