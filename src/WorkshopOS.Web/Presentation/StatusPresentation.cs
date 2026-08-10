using WorkshopOS.Application.Billing;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.RepairOrders;

namespace WorkshopOS.Web.Presentation;

public sealed record StatusBadgeModel(string Label, string CssClass);

public static class StatusPresentation
{
    public static StatusBadgeModel ForAppointment(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => new("Scheduled", "wos-badge wos-badge-neutral"),
        AppointmentStatus.Confirmed => new("Confirmed", "wos-badge wos-badge-info"),
        AppointmentStatus.CheckedIn => new("Checked in", "wos-badge wos-badge-primary"),
        AppointmentStatus.Completed => new("Completed", "wos-badge wos-badge-success"),
        AppointmentStatus.Cancelled => new("Cancelled", "wos-badge wos-badge-neutral"),
        AppointmentStatus.NoShow => new("No show", "wos-badge wos-badge-warning"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForRepairOrder(RepairOrderStatus status) => status switch
    {
        RepairOrderStatus.Draft => new("Draft", "wos-badge wos-badge-neutral"),
        RepairOrderStatus.Diagnosis => new("Diagnosis", "wos-badge wos-badge-info"),
        RepairOrderStatus.AwaitingApproval => new("Awaiting approval", "wos-badge wos-badge-warning"),
        RepairOrderStatus.Approved => new("Approved", "wos-badge wos-badge-primary"),
        RepairOrderStatus.InProgress => new("In progress", "wos-badge wos-badge-primary"),
        RepairOrderStatus.QualityControl => new("Quality control", "wos-badge wos-badge-info"),
        RepairOrderStatus.ReadyForPickup => new("Ready for pickup", "wos-badge wos-badge-success"),
        RepairOrderStatus.Completed => new("Completed", "wos-badge wos-badge-success"),
        RepairOrderStatus.Cancelled => new("Cancelled", "wos-badge wos-badge-neutral"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForRepairOrderPriority(RepairOrderPriority priority) => priority switch
    {
        RepairOrderPriority.Low => new("Low", "wos-badge wos-badge-neutral"),
        RepairOrderPriority.Normal => new("Normal", "wos-badge wos-badge-neutral"),
        RepairOrderPriority.High => new("High", "wos-badge wos-badge-warning"),
        RepairOrderPriority.Urgent => new("Urgent", "wos-badge wos-badge-danger"),
        _ => new(priority.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForTechnicianWork(TechnicianWorkStatus status) => status switch
    {
        TechnicianWorkStatus.Assigned => new("Assigned", "wos-badge wos-badge-info"),
        TechnicianWorkStatus.InProgress => new("In progress", "wos-badge wos-badge-primary"),
        TechnicianWorkStatus.WorkCompleted => new("Work completed", "wos-badge wos-badge-success"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForInspection(InspectionStatus status) => status switch
    {
        InspectionStatus.Draft => new("Draft", "wos-badge wos-badge-neutral"),
        InspectionStatus.InProgress => new("In progress", "wos-badge wos-badge-primary"),
        InspectionStatus.Completed => new("Completed", "wos-badge wos-badge-success"),
        InspectionStatus.Cancelled => new("Cancelled", "wos-badge wos-badge-neutral"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForInspectionCondition(InspectionCondition condition) => condition switch
    {
        InspectionCondition.NotChecked => new("Not checked", "wos-badge wos-badge-neutral wos-condition-unchecked"),
        InspectionCondition.Good => new("Good", "wos-badge wos-badge-success wos-condition-good"),
        InspectionCondition.Monitor => new("Monitor", "wos-badge wos-badge-info wos-condition-monitor"),
        InspectionCondition.Attention => new("Attention", "wos-badge wos-badge-warning wos-condition-attention"),
        InspectionCondition.Critical => new("Critical", "wos-badge wos-badge-danger wos-condition-critical"),
        _ => new(condition.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForEstimate(EstimateStatus status) => status switch
    {
        EstimateStatus.Draft => new("Draft", "wos-badge wos-badge-neutral"),
        EstimateStatus.Sent => new("Sent", "wos-badge wos-badge-warning"),
        EstimateStatus.Approved => new("Approved", "wos-badge wos-badge-success"),
        EstimateStatus.Declined => new("Declined", "wos-badge wos-badge-danger"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForInvoice(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => new("Draft", "wos-badge wos-badge-neutral"),
        InvoiceStatus.Issued => new("Issued", "wos-badge wos-badge-primary"),
        InvoiceStatus.Voided => new("Voided", "wos-badge wos-badge-neutral"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForPaymentState(InvoicePaymentState state) => state switch
    {
        InvoicePaymentState.Unpaid => new("Unpaid", "wos-badge wos-badge-warning"),
        InvoicePaymentState.PartiallyPaid => new("Partially paid", "wos-badge wos-badge-info"),
        InvoicePaymentState.Paid => new("Paid", "wos-badge wos-badge-success"),
        _ => new(state.ToString(), "wos-badge wos-badge-neutral"),
    };

    public static StatusBadgeModel ForActive(bool isActive) =>
        isActive
            ? new("Active", "wos-badge wos-badge-success")
            : new("Inactive", "wos-badge wos-badge-neutral");
}
