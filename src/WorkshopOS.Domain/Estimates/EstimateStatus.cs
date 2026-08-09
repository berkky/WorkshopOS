namespace WorkshopOS.Domain.Estimates;

public enum EstimateStatus
{
    Draft = 1,
    Sent = 2,
    PartiallyApproved = 3,
    Approved = 4,
    Declined = 5,
    Expired = 6,
    Superseded = 7,
}
