namespace WorkshopOS.Infrastructure.IntegrationTests;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public void Advance(TimeSpan duration)
    {
        _utcNow += duration;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
