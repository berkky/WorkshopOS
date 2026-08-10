using WorkshopOS.Domain.Organizations;

namespace WorkshopOS.Application.Memberships;

public static class MembershipRolePolicy
{
    public static bool IsSelfMutation(Guid actorUserId, Guid targetUserId) =>
        actorUserId == targetUserId;

    public static bool CanActorManageTargetRole(
        OrganizationMembershipRole actorRole,
        OrganizationMembershipRole targetCurrentRole)
    {
        return actorRole switch
        {
            OrganizationMembershipRole.Owner => true,
            OrganizationMembershipRole.Administrator =>
                targetCurrentRole is OrganizationMembershipRole.ServiceAdvisor
                    or OrganizationMembershipRole.Technician
                    or OrganizationMembershipRole.Viewer,
            _ => false,
        };
    }

    public static bool CanActorAssignRole(
        OrganizationMembershipRole actorRole,
        OrganizationMembershipRole newRole)
    {
        return actorRole switch
        {
            OrganizationMembershipRole.Owner => true,
            OrganizationMembershipRole.Administrator =>
                newRole is OrganizationMembershipRole.ServiceAdvisor
                    or OrganizationMembershipRole.Technician
                    or OrganizationMembershipRole.Viewer,
            _ => false,
        };
    }
}
