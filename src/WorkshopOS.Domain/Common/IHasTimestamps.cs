namespace WorkshopOS.Domain.Common;

public interface IHasTimestamps
{
    DateTimeOffset CreatedAtUtc { get; }

    DateTimeOffset UpdatedAtUtc { get; }
}
