using WorkshopOS.Application.Billing;

namespace WorkshopOS.Infrastructure.Billing;

public sealed class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    public string Generate(DateTimeOffset createdAtUtc)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        return $"INV-{createdAtUtc:yyyyMMdd}-{suffix}";
    }
}
