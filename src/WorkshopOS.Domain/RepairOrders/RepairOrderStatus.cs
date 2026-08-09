namespace WorkshopOS.Domain.RepairOrders;

public enum RepairOrderStatus
{
    Draft = 1,
    Diagnosis = 2,
    AwaitingApproval = 3,
    Approved = 4,
    InProgress = 5,
    QualityControl = 6,
    ReadyForPickup = 7,
    Completed = 8,
    Cancelled = 9,
}
