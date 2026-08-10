namespace WorkshopOS.Application.Onboarding;

public sealed record OwnerOnboardingCommand
{
    public required string OwnerDisplayName { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public required string OrganizationName { get; init; }

    public required string WorkshopLocationName { get; init; }

    public required string WorkshopLocationCode { get; init; }

    public required string TimeZoneId { get; init; }

    public required string CurrencyCode { get; init; }
}
