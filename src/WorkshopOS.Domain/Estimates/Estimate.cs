using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Estimates;

public class Estimate : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid RepairOrderId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public EstimateStatus Status { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public string? CustomerMessage { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public DateTimeOffset? DeclinedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected Estimate()
    {
    }

    public Estimate(
        Guid organizationId,
        Guid repairOrderId,
        string number,
        string currencyCode,
        EstimateStatus status = EstimateStatus.Draft,
        DateTimeOffset? expiresAtUtc = null,
        string? customerMessage = null)
        : base(organizationId)
    {
        if (repairOrderId == Guid.Empty)
        {
            throw new ArgumentException("Repair order identifier is required.", nameof(repairOrderId));
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Estimate number is required.", nameof(number));
        }

        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        }

        RepairOrderId = repairOrderId;
        Number = number.Trim();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        Status = status;
        ExpiresAtUtc = expiresAtUtc;
        CustomerMessage = string.IsNullOrWhiteSpace(customerMessage) ? null : customerMessage.Trim();
    }

    public void UpdateCustomerMessage(string? customerMessage)
    {
        if (Status != EstimateStatus.Draft)
        {
            throw new InvalidOperationException("Customer message can only be edited on draft estimates.");
        }

        CustomerMessage = string.IsNullOrWhiteSpace(customerMessage) ? null : customerMessage.Trim();
    }

    public void PresentForApproval(DateTimeOffset presentedAtUtc)
    {
        if (Status != EstimateStatus.Draft)
        {
            throw new InvalidOperationException("Only draft estimates can be presented for approval.");
        }

        Status = EstimateStatus.Sent;
        SentAtUtc = presentedAtUtc;
    }

    public void RecordCustomerApproval(DateTimeOffset approvedAtUtc)
    {
        if (Status != EstimateStatus.Sent)
        {
            throw new InvalidOperationException("Only sent estimates can record customer approval.");
        }

        Status = EstimateStatus.Approved;
        ApprovedAtUtc = approvedAtUtc;
    }

    public void RecordCustomerDecline(DateTimeOffset declinedAtUtc)
    {
        if (Status != EstimateStatus.Sent)
        {
            throw new InvalidOperationException("Only sent estimates can record customer decline.");
        }

        Status = EstimateStatus.Declined;
        DeclinedAtUtc = declinedAtUtc;
    }
}
