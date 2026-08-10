namespace WorkshopOS.Application.Estimates;

public interface IEstimateNumberGenerator
{
    string Generate(DateTimeOffset createdAtUtc);
}
