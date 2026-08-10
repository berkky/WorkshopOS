using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Application.RepairOrders;

public static class RepairOrderManagerPolicy
{
    public static bool CanManageRepairOrders(OrganizationMembershipRole role) =>
        role is OrganizationMembershipRole.Owner
            or OrganizationMembershipRole.Administrator
            or OrganizationMembershipRole.ServiceAdvisor;
}

public static class RepairOrderLifecyclePolicy
{
    public static bool IsTerminal(RepairOrderStatus status) =>
        status is RepairOrderStatus.Completed or RepairOrderStatus.Cancelled;

    public static bool CanStartWork(RepairOrderStatus status) =>
        status is RepairOrderStatus.Draft;

    public static bool CanComplete(RepairOrderStatus status) =>
        status is RepairOrderStatus.InProgress;

    public static bool CanCancel(RepairOrderStatus status) =>
        status is RepairOrderStatus.Draft or RepairOrderStatus.InProgress;
}
