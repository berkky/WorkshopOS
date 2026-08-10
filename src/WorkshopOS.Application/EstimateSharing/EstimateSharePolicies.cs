using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.EstimateSharing;

public static class EstimateShareManagerPolicy
{
    public static bool CanManageEstimateShares(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;
}

public static class EstimateShareEligibilityPolicy
{
    public static bool CanCreateShare(EstimateStatus status) =>
        status is EstimateStatus.Sent
            or EstimateStatus.Approved
            or EstimateStatus.Declined;

    public static bool IsCustomerVisible(EstimateStatus status) =>
        CanCreateShare(status);
}

public static class EstimateShareExpiryPolicy
{
    public const int DefaultDurationDays = 7;

    public const int MinimumDurationDays = 1;

    public const int MaximumDurationDays = 30;

    public static bool IsValidDurationDays(int durationDays) =>
        durationDays is >= MinimumDurationDays and <= MaximumDurationDays;
}
