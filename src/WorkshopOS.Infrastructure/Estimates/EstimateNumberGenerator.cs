using WorkshopOS.Application.Estimates;

namespace WorkshopOS.Infrastructure.Estimates;

public sealed class EstimateNumberGenerator : IEstimateNumberGenerator
{
    public string Generate(DateTimeOffset createdAtUtc)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        return $"EST-{createdAtUtc:yyyyMMdd}-{suffix}";
    }
}
