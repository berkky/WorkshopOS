namespace WorkshopOS.Application.Billing;

public static class BillingManagerPolicy
{
    public static bool CanManageBilling(Domain.Organizations.OrganizationMembershipRole role) =>
        role is Domain.Organizations.OrganizationMembershipRole.Owner
            or Domain.Organizations.OrganizationMembershipRole.Administrator
            or Domain.Organizations.OrganizationMembershipRole.ServiceAdvisor;
}

public enum InvoicePaymentState
{
    Unpaid = 1,
    PartiallyPaid = 2,
    Paid = 3,
}

public static class InvoiceLifecyclePolicy
{
    public static bool IsFinanciallyMutable(Domain.Billing.InvoiceStatus status) =>
        status is Domain.Billing.InvoiceStatus.Draft;

    public static bool CanIssue(Domain.Billing.InvoiceStatus status) =>
        status is Domain.Billing.InvoiceStatus.Draft;

    public static bool CanRecordPayment(Domain.Billing.InvoiceStatus status) =>
        status is Domain.Billing.InvoiceStatus.Issued;

    public static bool CanVoid(Domain.Billing.InvoiceStatus status) =>
        status is Domain.Billing.InvoiceStatus.Draft or Domain.Billing.InvoiceStatus.Issued;
}

public static class InvoiceInputValidator
{
    public const int MaxDescriptionLength = 500;

    public const int MaxCommercialNotesLength = 2000;

    public const int MaxReferenceLength = 100;

    public const int MaxNoteLength = 500;

    public const int MaxItemsPerInvoice = 100;

    public static bool TryNormalizeDescription(string? description, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        normalized = description.Trim();
        return normalized.Length <= MaxDescriptionLength;
    }

    public static bool TryNormalizeCommercialNotes(string? notes, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            normalized = null;
            return true;
        }

        normalized = notes.Trim();
        return normalized.Length <= MaxCommercialNotesLength;
    }

    public static bool TryNormalizeReference(string? reference, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            normalized = null;
            return true;
        }

        normalized = reference.Trim();
        return normalized.Length <= MaxReferenceLength;
    }

    public static bool TryNormalizeNote(string? note, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            normalized = null;
            return true;
        }

        normalized = note.Trim();
        return normalized.Length <= MaxNoteLength;
    }

    public static bool IsValidQuantity(decimal quantity) => quantity > 0;

    public static bool IsValidUnitPrice(decimal unitPrice) => unitPrice >= 0;

    public static bool IsValidPaymentAmount(decimal amount) => amount > 0;
}

public static class InvoiceMoneyCalculator
{
    public static decimal CalculateLineTotal(decimal quantity, decimal unitPrice) =>
        decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);

    public static decimal CalculateInvoiceTotal(IReadOnlyList<(decimal Quantity, decimal UnitPrice)> items)
    {
        decimal total = 0;
        foreach (var item in items)
        {
            total += CalculateLineTotal(item.Quantity, item.UnitPrice);
        }

        return total;
    }

    public static InvoicePaymentState DerivePaymentState(decimal total, decimal amountPaid)
    {
        if (amountPaid <= 0)
        {
            return InvoicePaymentState.Unpaid;
        }

        if (amountPaid >= total)
        {
            return InvoicePaymentState.Paid;
        }

        return InvoicePaymentState.PartiallyPaid;
    }
}
