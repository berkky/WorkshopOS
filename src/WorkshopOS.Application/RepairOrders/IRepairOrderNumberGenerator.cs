namespace WorkshopOS.Application.RepairOrders;

public interface IRepairOrderNumberGenerator
{
    string Generate(DateTimeOffset openedAtUtc);
}
