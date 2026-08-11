using Microsoft.Extensions.Localization;
using WorkshopOS.Application.Billing;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Web;

namespace WorkshopOS.Web.Presentation;

public sealed record StatusBadgeModel(string Label, string CssClass);

public interface IStatusPresentation
{
    StatusBadgeModel ForAppointment(AppointmentStatus status);
    StatusBadgeModel ForRepairOrder(RepairOrderStatus status);
    StatusBadgeModel ForRepairOrderPriority(RepairOrderPriority priority);
    StatusBadgeModel ForTechnicianWork(TechnicianWorkStatus status);
    StatusBadgeModel ForInspection(InspectionStatus status);
    StatusBadgeModel ForInspectionCondition(InspectionCondition condition);
    StatusBadgeModel ForEstimate(EstimateStatus status);
    StatusBadgeModel ForInvoice(InvoiceStatus status);
    StatusBadgeModel ForPaymentState(InvoicePaymentState state);
    StatusBadgeModel ForActive(bool isActive);
}

public sealed class LocalizedStatusPresentation(IStringLocalizer<SharedResource> localizer) : IStatusPresentation
{
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;

    public StatusBadgeModel ForAppointment(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => Badge("Status_Appointment_Scheduled", "wos-badge wos-badge-neutral"),
        AppointmentStatus.Confirmed => Badge("Status_Appointment_Confirmed", "wos-badge wos-badge-info"),
        AppointmentStatus.CheckedIn => Badge("Status_Appointment_CheckedIn", "wos-badge wos-badge-primary"),
        AppointmentStatus.Completed => Badge("Status_Appointment_Completed", "wos-badge wos-badge-success"),
        AppointmentStatus.Cancelled => Badge("Status_Appointment_Cancelled", "wos-badge wos-badge-neutral"),
        AppointmentStatus.NoShow => Badge("Status_Appointment_NoShow", "wos-badge wos-badge-warning"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForRepairOrder(RepairOrderStatus status) => status switch
    {
        RepairOrderStatus.Draft => Badge("Status_RepairOrder_Draft", "wos-badge wos-badge-neutral"),
        RepairOrderStatus.Diagnosis => Badge("Status_RepairOrder_Diagnosis", "wos-badge wos-badge-info"),
        RepairOrderStatus.AwaitingApproval => Badge("Status_RepairOrder_AwaitingApproval", "wos-badge wos-badge-warning"),
        RepairOrderStatus.Approved => Badge("Status_RepairOrder_Approved", "wos-badge wos-badge-primary"),
        RepairOrderStatus.InProgress => Badge("Status_RepairOrder_InProgress", "wos-badge wos-badge-primary"),
        RepairOrderStatus.QualityControl => Badge("Status_RepairOrder_QualityControl", "wos-badge wos-badge-info"),
        RepairOrderStatus.ReadyForPickup => Badge("Status_RepairOrder_ReadyForPickup", "wos-badge wos-badge-success"),
        RepairOrderStatus.Completed => Badge("Status_RepairOrder_Completed", "wos-badge wos-badge-success"),
        RepairOrderStatus.Cancelled => Badge("Status_RepairOrder_Cancelled", "wos-badge wos-badge-neutral"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForRepairOrderPriority(RepairOrderPriority priority) => priority switch
    {
        RepairOrderPriority.Low => Badge("Status_Priority_Low", "wos-badge wos-badge-neutral"),
        RepairOrderPriority.Normal => Badge("Status_Priority_Normal", "wos-badge wos-badge-neutral"),
        RepairOrderPriority.High => Badge("Status_Priority_High", "wos-badge wos-badge-warning"),
        RepairOrderPriority.Urgent => Badge("Status_Priority_Urgent", "wos-badge wos-badge-danger"),
        _ => new(priority.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForTechnicianWork(TechnicianWorkStatus status) => status switch
    {
        TechnicianWorkStatus.Assigned => Badge("Status_TechnicianWork_Assigned", "wos-badge wos-badge-info"),
        TechnicianWorkStatus.InProgress => Badge("Status_TechnicianWork_InProgress", "wos-badge wos-badge-primary"),
        TechnicianWorkStatus.WorkCompleted => Badge("Status_TechnicianWork_WorkCompleted", "wos-badge wos-badge-success"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForInspection(InspectionStatus status) => status switch
    {
        InspectionStatus.Draft => Badge("Status_Inspection_Draft", "wos-badge wos-badge-neutral"),
        InspectionStatus.InProgress => Badge("Status_Inspection_InProgress", "wos-badge wos-badge-primary"),
        InspectionStatus.Completed => Badge("Status_Inspection_Completed", "wos-badge wos-badge-success"),
        InspectionStatus.Cancelled => Badge("Status_Inspection_Cancelled", "wos-badge wos-badge-neutral"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForInspectionCondition(InspectionCondition condition) => condition switch
    {
        InspectionCondition.NotChecked => Badge("Status_InspectionCondition_NotChecked", "wos-badge wos-badge-neutral wos-condition-unchecked"),
        InspectionCondition.Good => Badge("Status_InspectionCondition_Good", "wos-badge wos-badge-success wos-condition-good"),
        InspectionCondition.Monitor => Badge("Status_InspectionCondition_Monitor", "wos-badge wos-badge-info wos-condition-monitor"),
        InspectionCondition.Attention => Badge("Status_InspectionCondition_Attention", "wos-badge wos-badge-warning wos-condition-attention"),
        InspectionCondition.Critical => Badge("Status_InspectionCondition_Critical", "wos-badge wos-badge-danger wos-condition-critical"),
        _ => new(condition.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForEstimate(EstimateStatus status) => status switch
    {
        EstimateStatus.Draft => Badge("Status_Estimate_Draft", "wos-badge wos-badge-neutral"),
        EstimateStatus.Sent => Badge("Status_Estimate_Sent", "wos-badge wos-badge-warning"),
        EstimateStatus.Approved => Badge("Status_Estimate_Approved", "wos-badge wos-badge-success"),
        EstimateStatus.Declined => Badge("Status_Estimate_Declined", "wos-badge wos-badge-danger"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForInvoice(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => Badge("Status_Invoice_Draft", "wos-badge wos-badge-neutral"),
        InvoiceStatus.Issued => Badge("Status_Invoice_Issued", "wos-badge wos-badge-primary"),
        InvoiceStatus.Voided => Badge("Status_Invoice_Voided", "wos-badge wos-badge-neutral"),
        _ => new(status.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForPaymentState(InvoicePaymentState state) => state switch
    {
        InvoicePaymentState.Unpaid => Badge("Status_Payment_Unpaid", "wos-badge wos-badge-warning"),
        InvoicePaymentState.PartiallyPaid => Badge("Status_Payment_PartiallyPaid", "wos-badge wos-badge-info"),
        InvoicePaymentState.Paid => Badge("Status_Payment_Paid", "wos-badge wos-badge-success"),
        _ => new(state.ToString(), "wos-badge wos-badge-neutral"),
    };

    public StatusBadgeModel ForActive(bool isActive) =>
        isActive
            ? Badge("Status_Active", "wos-badge wos-badge-success")
            : Badge("Status_Inactive", "wos-badge wos-badge-neutral");

    private StatusBadgeModel Badge(string key, string cssClass) =>
        new(_localizer[key].Value, cssClass);
}
