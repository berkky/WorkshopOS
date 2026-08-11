using Microsoft.Extensions.Localization;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Application.Billing;
using WorkshopOS.Application.Catalog;
using WorkshopOS.Application.CustomerPortal;
using WorkshopOS.Application.Customers;
using WorkshopOS.Application.Estimates;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Application.InspectionMedia;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.Inventory;
using WorkshopOS.Application.Operations;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Application.Team;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Web;

namespace WorkshopOS.Web.Localization;

public interface IWebFailureMessages
{
    string Appointment(AppointmentOperationFailureReason? reason);
    string Customer(CustomerOperationFailureReason? reason);
    string Vehicle(VehicleOperationFailureReason? reason);
    string RepairOrder(RepairOrderOperationFailureReason? reason);
    string Inspection(InspectionOperationFailureReason? reason);
    string InspectionMedia(InspectionMediaOperationFailureReason? reason);
    string Estimate(EstimateOperationFailureReason? reason);
    string EstimateShare(EstimateShareOperationFailureReason? reason);
    string Invoice(InvoiceOperationFailureReason? reason);
    string Payment(PaymentOperationFailureReason? reason);
    string Inventory(InventoryAdjustmentFailureReason? reason);
    string Catalog(CatalogOperationFailureReason? reason);
    string Operations(WorkshopOperationFailureReason? reason);
    string Team(TeamManagementFailureReason? reason);
    string Portal(CustomerPortalOperationFailureReason? reason);
    string InvalidReportingDateRange();
    string InspectionChecklistRequired();
    string InspectionPhotoRequired();
    string LoginInvalidCredentials();
    string OnboardingInvalidInput();
    string OnboardingFailed();
}

public sealed class WebFailureMessages(IStringLocalizer<SharedResource> localizer) : IWebFailureMessages
{
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;

    public string Appointment(AppointmentOperationFailureReason? reason) => reason switch
    {
        AppointmentOperationFailureReason.InvalidInput => T("Failure_Appointment_InvalidInput"),
        AppointmentOperationFailureReason.WorkshopLocationNotFound => T("Failure_Appointment_WorkshopLocationNotFound"),
        AppointmentOperationFailureReason.CustomerNotFound => T("Failure_Appointment_CustomerNotFound"),
        AppointmentOperationFailureReason.VehicleNotFound => T("Failure_Appointment_VehicleNotFound"),
        AppointmentOperationFailureReason.CustomerVehicleMismatch => T("Failure_Appointment_CustomerVehicleMismatch"),
        AppointmentOperationFailureReason.PastStartNotAllowed => T("Failure_Appointment_PastStartNotAllowed"),
        AppointmentOperationFailureReason.VehicleOverlap => T("Failure_Appointment_VehicleOverlap"),
        AppointmentOperationFailureReason.ConcurrencyConflict => T("Failure_Appointment_ConcurrencyConflict"),
        AppointmentOperationFailureReason.CannotModifyCancelled => T("Failure_Appointment_CannotModifyCancelled"),
        AppointmentOperationFailureReason.OrganizationUnresolved => T("Failure_OrganizationUnresolved"),
        _ => T("Failure_Appointment_Generic"),
    };

    public string Customer(CustomerOperationFailureReason? reason) => reason switch
    {
        CustomerOperationFailureReason.InvalidInput => T("Failure_Customer_InvalidInput"),
        CustomerOperationFailureReason.OrganizationUnresolved => T("Failure_OrganizationUnavailable"),
        _ => T("Failure_Customer_Generic"),
    };

    public string Vehicle(VehicleOperationFailureReason? reason) => reason switch
    {
        VehicleOperationFailureReason.InvalidInput => T("Failure_Vehicle_InvalidInput"),
        VehicleOperationFailureReason.CustomerNotFound => T("Failure_Vehicle_CustomerNotFound"),
        VehicleOperationFailureReason.OrganizationUnresolved => T("Failure_OrganizationUnavailable"),
        _ => T("Failure_Vehicle_Generic"),
    };

    public string RepairOrder(RepairOrderOperationFailureReason? reason) => reason switch
    {
        RepairOrderOperationFailureReason.OrganizationUnresolved => T("Failure_RepairOrder_OrganizationUnresolved"),
        RepairOrderOperationFailureReason.InvalidInput => T("Failure_InvalidInput"),
        RepairOrderOperationFailureReason.WorkshopLocationNotFound => T("Failure_RepairOrder_WorkshopLocationNotFound"),
        RepairOrderOperationFailureReason.CustomerNotFound => T("Failure_RepairOrder_CustomerNotFound"),
        RepairOrderOperationFailureReason.VehicleNotFound => T("Failure_RepairOrder_VehicleNotFound"),
        RepairOrderOperationFailureReason.AppointmentNotFound => T("Failure_RepairOrder_AppointmentNotFound"),
        RepairOrderOperationFailureReason.CustomerVehicleMismatch => T("Failure_RepairOrder_CustomerVehicleMismatch"),
        RepairOrderOperationFailureReason.DuplicateAppointmentRepairOrder => T("Failure_RepairOrder_DuplicateAppointment"),
        RepairOrderOperationFailureReason.ConcurrencyConflict => T("Failure_RepairOrder_ConcurrencyConflict"),
        RepairOrderOperationFailureReason.InvalidLifecycleTransition => T("Failure_RepairOrder_InvalidLifecycleTransition"),
        RepairOrderOperationFailureReason.RepairOrderNotFound => T("Failure_RepairOrder_NotFound"),
        _ => T("Failure_RepairOrder_Generic"),
    };

    public string Inspection(InspectionOperationFailureReason? reason) => reason switch
    {
        InspectionOperationFailureReason.OrganizationUnresolved => T("Failure_Inspection_OrganizationUnresolved"),
        InspectionOperationFailureReason.InvalidInput => T("Failure_InvalidInput"),
        InspectionOperationFailureReason.RepairOrderNotEligible => T("Failure_Inspection_RepairOrderNotEligible"),
        InspectionOperationFailureReason.InvalidLifecycleTransition => T("Failure_Inspection_InvalidLifecycleTransition"),
        InspectionOperationFailureReason.ItemsNotFullyInspected => T("Failure_Inspection_ItemsNotFullyInspected"),
        InspectionOperationFailureReason.EmptyInspection => T("Failure_Inspection_EmptyInspection"),
        InspectionOperationFailureReason.ConcurrencyConflict => T("Failure_Inspection_ConcurrencyConflict"),
        InspectionOperationFailureReason.ItemNotFound => T("Failure_Inspection_ItemNotFound"),
        InspectionOperationFailureReason.DuplicateItemSubmission => T("Failure_Inspection_DuplicateItemSubmission"),
        _ => T("Failure_Inspection_Generic"),
    };

    public string InspectionMedia(InspectionMediaOperationFailureReason? reason) => reason switch
    {
        InspectionMediaOperationFailureReason.UnsupportedMediaType => T("Failure_InspectionMedia_UnsupportedMediaType"),
        InspectionMediaOperationFailureReason.PhotoLimitExceeded => T("Failure_InspectionMedia_PhotoLimitExceeded"),
        InspectionMediaOperationFailureReason.InvalidInput => T("Failure_InspectionMedia_InvalidInput"),
        InspectionMediaOperationFailureReason.InvalidLifecycleTransition => T("Failure_InspectionMedia_InvalidLifecycleTransition"),
        _ => T("Failure_InspectionMedia_Generic"),
    };

    public string Estimate(EstimateOperationFailureReason? reason) => reason switch
    {
        EstimateOperationFailureReason.OrganizationUnresolved => T("Failure_OrganizationUnresolved"),
        EstimateOperationFailureReason.EstimateNotFound => T("Failure_Estimate_NotFound"),
        EstimateOperationFailureReason.RepairOrderNotFound => T("Failure_Estimate_RepairOrderNotFound"),
        EstimateOperationFailureReason.Unauthorized => T("Failure_Estimate_Unauthorized"),
        EstimateOperationFailureReason.InvalidInput => T("Failure_Estimate_InvalidInput"),
        EstimateOperationFailureReason.RepairOrderNotEligible => T("Failure_Estimate_RepairOrderNotEligible"),
        EstimateOperationFailureReason.InvalidLifecycleTransition => T("Failure_Estimate_InvalidLifecycleTransition"),
        EstimateOperationFailureReason.EmptyEstimate => T("Failure_Estimate_EmptyEstimate"),
        EstimateOperationFailureReason.ConcurrencyConflict => T("Failure_Estimate_ConcurrencyConflict"),
        EstimateOperationFailureReason.ItemNotFound => T("Failure_Estimate_ItemNotFound"),
        EstimateOperationFailureReason.ItemLimitExceeded => T("Failure_Estimate_ItemLimitExceeded"),
        EstimateOperationFailureReason.ItemBelongsToAnotherEstimate => T("Failure_Estimate_ItemBelongsToAnotherEstimate"),
        _ => T("Failure_Estimate_Generic"),
    };

    public string EstimateShare(EstimateShareOperationFailureReason? reason) => reason switch
    {
        EstimateShareOperationFailureReason.EstimateNotEligible => T("Failure_EstimateShare_NotEligible"),
        EstimateShareOperationFailureReason.ActiveShareAlreadyExists => T("Failure_EstimateShare_ActiveShareExists"),
        EstimateShareOperationFailureReason.ShareNotFound => T("Failure_EstimateShare_NotFound"),
        EstimateShareOperationFailureReason.Unauthorized => T("Failure_EstimateShare_Unauthorized"),
        EstimateShareOperationFailureReason.InvalidInput => T("Failure_EstimateShare_InvalidInput"),
        _ => T("Failure_EstimateShare_Generic"),
    };

    public string Invoice(InvoiceOperationFailureReason? reason) => reason switch
    {
        InvoiceOperationFailureReason.OrganizationUnresolved => T("Failure_OrganizationUnresolved"),
        InvoiceOperationFailureReason.InvoiceNotFound => T("Failure_Invoice_NotFound"),
        InvoiceOperationFailureReason.RepairOrderNotFound => T("Failure_Invoice_RepairOrderNotFound"),
        InvoiceOperationFailureReason.EstimateNotFound => T("Failure_Invoice_EstimateNotFound"),
        InvoiceOperationFailureReason.Unauthorized => T("Failure_Invoice_Unauthorized"),
        InvoiceOperationFailureReason.InvalidInput => T("Failure_Invoice_InvalidInput"),
        InvoiceOperationFailureReason.RepairOrderNotEligible => T("Failure_Invoice_RepairOrderNotEligible"),
        InvoiceOperationFailureReason.EstimateNotEligible => T("Failure_Invoice_EstimateNotEligible"),
        InvoiceOperationFailureReason.InvalidLifecycleTransition => T("Failure_Invoice_InvalidLifecycleTransition"),
        InvoiceOperationFailureReason.EmptyInvoice => T("Failure_Invoice_EmptyInvoice"),
        InvoiceOperationFailureReason.ConcurrencyConflict => T("Failure_Invoice_ConcurrencyConflict"),
        InvoiceOperationFailureReason.ItemNotFound => T("Failure_Invoice_ItemNotFound"),
        InvoiceOperationFailureReason.ItemLimitExceeded => T("Failure_Invoice_ItemLimitExceeded"),
        InvoiceOperationFailureReason.CurrentInvoiceExists => T("Failure_Invoice_CurrentInvoiceExists"),
        InvoiceOperationFailureReason.SourceEstimateInvoiceExists => T("Failure_Invoice_SourceEstimateInvoiceExists"),
        _ => T("Failure_Invoice_Generic"),
    };

    public string Payment(PaymentOperationFailureReason? reason) => reason switch
    {
        PaymentOperationFailureReason.OrganizationUnresolved => T("Failure_OrganizationUnresolved"),
        PaymentOperationFailureReason.Unauthorized => T("Failure_Payment_Unauthorized"),
        PaymentOperationFailureReason.InvalidInput => T("Failure_Payment_InvalidInput"),
        PaymentOperationFailureReason.InvoiceNotFound => T("Failure_Invoice_NotFound"),
        PaymentOperationFailureReason.InvalidLifecycleTransition => T("Failure_Payment_InvalidLifecycleTransition"),
        PaymentOperationFailureReason.Overpayment => T("Failure_Payment_Overpayment"),
        PaymentOperationFailureReason.ConcurrencyConflict => T("Failure_Payment_ConcurrencyConflict"),
        PaymentOperationFailureReason.CommerciallyClosed => T("Failure_Payment_CommerciallyClosed"),
        _ => T("Failure_Payment_Generic"),
    };

    public string Inventory(InventoryAdjustmentFailureReason? reason) => reason switch
    {
        InventoryAdjustmentFailureReason.InsufficientStock => T("Failure_Inventory_InsufficientStock"),
        InventoryAdjustmentFailureReason.PartInactive => T("Failure_Inventory_PartInactive"),
        InventoryAdjustmentFailureReason.LocationInactive => T("Failure_Inventory_LocationInactive"),
        InventoryAdjustmentFailureReason.InvalidInput => T("Failure_Inventory_InvalidInput"),
        InventoryAdjustmentFailureReason.Unauthorized => T("Failure_Inventory_Unauthorized"),
        InventoryAdjustmentFailureReason.ConcurrencyConflict => T("Failure_Inventory_ConcurrencyConflict"),
        _ => T("Failure_Inventory_Generic"),
    };

    public string Catalog(CatalogOperationFailureReason? reason) => reason switch
    {
        CatalogOperationFailureReason.DuplicateCode => T("Failure_Catalog_DuplicateCode"),
        CatalogOperationFailureReason.DuplicateSku => T("Failure_Catalog_DuplicateSku"),
        CatalogOperationFailureReason.HasPositiveStock => T("Failure_Catalog_HasPositiveStock"),
        CatalogOperationFailureReason.InvalidInput => T("Failure_InvalidInput"),
        CatalogOperationFailureReason.Unauthorized => T("Failure_Catalog_Unauthorized"),
        CatalogOperationFailureReason.ItemNotFound => T("Failure_Catalog_ItemNotFound"),
        _ => T("Failure_Catalog_Generic"),
    };

    public string Operations(WorkshopOperationFailureReason? reason) => reason switch
    {
        WorkshopOperationFailureReason.TerminalRepairOrder => T("Failure_Operations_TerminalRepairOrder"),
        WorkshopOperationFailureReason.StaffMemberNotFound => T("Failure_Operations_StaffMemberNotFound"),
        WorkshopOperationFailureReason.TechnicianNotEligible => T("Failure_Operations_TechnicianNotEligible"),
        WorkshopOperationFailureReason.LocationMismatch => T("Failure_Operations_LocationMismatch"),
        WorkshopOperationFailureReason.DuplicateActiveAssignment => T("Failure_Operations_DuplicateActiveAssignment"),
        WorkshopOperationFailureReason.AssignmentNotFound => T("Failure_Operations_AssignmentNotFound"),
        WorkshopOperationFailureReason.StaffInactive => T("Failure_Operations_StaffInactive"),
        _ => T("Failure_Operations_Generic"),
    };

    public string Team(TeamManagementFailureReason? reason) => reason switch
    {
        TeamManagementFailureReason.LinkedUserNotMember => T("Failure_Team_LinkedUserNotMember"),
        TeamManagementFailureReason.LinkedUserAlreadyAssigned => T("Failure_Team_LinkedUserAlreadyAssigned"),
        TeamManagementFailureReason.WorkshopLocationNotFound => T("Failure_Team_WorkshopLocationNotFound"),
        TeamManagementFailureReason.InvalidInput => T("Failure_Team_InvalidInput"),
        _ => T("Failure_Team_Generic"),
    };

    public string Portal(CustomerPortalOperationFailureReason? reason) => reason switch
    {
        CustomerPortalOperationFailureReason.InvalidLifecycleTransition => T("Failure_Portal_InvalidLifecycleTransition"),
        CustomerPortalOperationFailureReason.ConcurrencyConflict => T("Failure_Portal_ConcurrencyConflict"),
        _ => T("Failure_Portal_Generic"),
    };

    public string InvalidReportingDateRange() => T("Failure_InvalidReportingDateRange");
    public string InspectionChecklistRequired() => T("Failure_Inspection_ChecklistRequired");
    public string InspectionPhotoRequired() => T("Failure_Inspection_PhotoRequired");
    public string LoginInvalidCredentials() => T("Message_Login_InvalidCredentials");
    public string OnboardingInvalidInput() => T("Message_Onboarding_InvalidInput");
    public string OnboardingFailed() => T("Message_Onboarding_Failed");

    private string T(string key) => _localizer[key].Value;
}
