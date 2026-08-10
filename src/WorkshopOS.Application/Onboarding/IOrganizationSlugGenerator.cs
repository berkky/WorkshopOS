namespace WorkshopOS.Application.Onboarding;

public interface IOrganizationSlugGenerator
{
    string GenerateSlug(string organizationName, Guid organizationId);
}
