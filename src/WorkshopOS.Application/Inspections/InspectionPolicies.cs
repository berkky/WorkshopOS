namespace WorkshopOS.Application.Inspections;

public static class InspectionManagerPolicy
{
    public static bool CanManageInspections(Domain.Organizations.OrganizationMembershipRole role) =>
        role is Domain.Organizations.OrganizationMembershipRole.Owner
            or Domain.Organizations.OrganizationMembershipRole.Administrator
            or Domain.Organizations.OrganizationMembershipRole.ServiceAdvisor;
}

public static class InspectionLifecyclePolicy
{
    public static bool IsTerminal(Domain.Inspections.InspectionStatus status) =>
        status is Domain.Inspections.InspectionStatus.Completed
            or Domain.Inspections.InspectionStatus.Cancelled;

    public static bool CanStart(Domain.Inspections.InspectionStatus status) =>
        status is Domain.Inspections.InspectionStatus.Draft
            or Domain.Inspections.InspectionStatus.InProgress;

    public static bool CanUpdateItems(Domain.Inspections.InspectionStatus status) =>
        status is Domain.Inspections.InspectionStatus.InProgress;

    public static bool CanComplete(Domain.Inspections.InspectionStatus status) =>
        status is Domain.Inspections.InspectionStatus.InProgress;

    public static bool IsInspected(Domain.Inspections.InspectionCondition condition) =>
        condition != Domain.Inspections.InspectionCondition.NotChecked;
}

public static class DefaultVehicleInspectionTemplate
{
    public static IReadOnlyList<VehicleInspectionTemplateItem> Items { get; } =
        new List<VehicleInspectionTemplateItem>
        {
            new("Exterior & Body", "Body condition", 10),
            new("Exterior & Body", "Windshield / glass", 20),
            new("Exterior & Body", "Wipers", 30),
            new("Tires & Wheels", "Front tires", 40),
            new("Tires & Wheels", "Rear tires", 50),
            new("Tires & Wheels", "Wheel condition", 60),
            new("Brakes", "Front brakes", 70),
            new("Brakes", "Rear brakes", 80),
            new("Brakes", "Parking brake", 90),
            new("Engine Bay / Fluids", "Engine oil", 100),
            new("Engine Bay / Fluids", "Coolant", 110),
            new("Engine Bay / Fluids", "Brake fluid", 120),
            new("Engine Bay / Fluids", "Visible leaks", 130),
            new("Lights & Electrical", "Headlights", 140),
            new("Lights & Electrical", "Brake lights", 150),
            new("Lights & Electrical", "Indicators", 160),
            new("Lights & Electrical", "Battery visual condition", 170),
            new("Interior & Safety", "Seat belts", 180),
            new("Interior & Safety", "Warning lights", 190),
            new("Interior & Safety", "Horn", 200),
        };
}

public sealed record VehicleInspectionTemplateItem(string Section, string Name, int SortOrder);
