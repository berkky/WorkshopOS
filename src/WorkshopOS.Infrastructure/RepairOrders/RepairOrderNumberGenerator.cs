using WorkshopOS.Application.RepairOrders;

namespace WorkshopOS.Infrastructure.RepairOrders;

public sealed class RepairOrderNumberGenerator : IRepairOrderNumberGenerator
{
    public string Generate(DateTimeOffset openedAtUtc)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        return $"RO-{openedAtUtc:yyyyMMdd}-{suffix}";
    }
}
