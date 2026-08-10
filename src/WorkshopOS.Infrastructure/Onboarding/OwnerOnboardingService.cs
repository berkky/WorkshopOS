using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WorkshopOS.Application.Onboarding;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Onboarding;

public sealed class OwnerOnboardingService : IOwnerOnboardingService
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOrganizationSlugGenerator _slugGenerator;
    private readonly TrustedOrganizationMembershipValidator _membershipValidator;
    private readonly IOrganizationContextMutator _organizationContextMutator;
    private readonly IOwnerOnboardingPostIdentityGate _postIdentityGate;

    public OwnerOnboardingService(
        AppDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IOrganizationSlugGenerator slugGenerator,
        TrustedOrganizationMembershipValidator membershipValidator,
        IOrganizationContextMutator organizationContextMutator,
        IOwnerOnboardingPostIdentityGate postIdentityGate)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _slugGenerator = slugGenerator;
        _membershipValidator = membershipValidator;
        _organizationContextMutator = organizationContextMutator;
        _postIdentityGate = postIdentityGate;
    }

    public async Task<OwnerOnboardingResult> OnboardOwnerAsync(
        OwnerOnboardingCommand command,
        CancellationToken cancellationToken = default)
    {
        var displayName = OnboardingInputValidator.NormalizeOwnerDisplayName(command.OwnerDisplayName);
        var email = OnboardingInputValidator.NormalizeEmail(command.Email);
        var organizationName = OnboardingInputValidator.NormalizeOrganizationName(command.OrganizationName);
        var locationName = OnboardingInputValidator.NormalizeWorkshopLocationName(command.WorkshopLocationName);
        var locationCode = OnboardingInputValidator.NormalizeLocationCode(command.WorkshopLocationCode);
        var timeZoneId = command.TimeZoneId.Trim();

        if (string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(organizationName)
            || string.IsNullOrWhiteSpace(locationName)
            || string.IsNullOrWhiteSpace(locationCode))
        {
            return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.InvalidInput);
        }

        if (!OnboardingInputValidator.TryValidateTimeZoneId(timeZoneId, out _))
        {
            return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.InvalidInput);
        }

        if (!OnboardingInputValidator.TryValidateCurrencyCode(command.CurrencyCode, out var currencyCode, out _))
        {
            return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.InvalidInput);
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = false,
                DisplayName = displayName,
            };

            var createUserResult = await _userManager.CreateAsync(user, command.Password);
            if (!createUserResult.Succeeded)
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                if (createUserResult.Errors.Any(error =>
                        error.Code is "DuplicateUserName" or "DuplicateEmail"))
                {
                    return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.DuplicateAccount);
                }

                return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.AccountCreationFailed);
            }

            try
            {
                await _postIdentityGate.EnsureCanContinueAsync(cancellationToken);
            }
            catch
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.UnexpectedFailure);
            }

            var organizationId = Guid.CreateVersion7();
            var slug = _slugGenerator.GenerateSlug(organizationName, organizationId);
            var organization = new Organization(
                organizationId,
                organizationName,
                slug,
                currencyCode,
                timeZoneId);

            var membership = new OrganizationMembership(
                organization.Id,
                user.Id,
                OrganizationMembershipRole.Owner);

            var workshopLocation = new WorkshopLocation(
                organization.Id,
                locationName,
                locationCode,
                timeZoneId: null,
                isActive: true);

            _dbContext.Organizations.Add(organization);
            _dbContext.OrganizationMemberships.Add(membership);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.OrganizationCreationFailed);
            }

            var hasTrustedMembership = await _membershipValidator.HasActiveMembershipAsync(
                user.Id,
                organization.Id,
                OrganizationMembershipRole.Owner,
                cancellationToken);

            if (!hasTrustedMembership)
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.UnexpectedFailure);
            }

            _organizationContextMutator.Resolve(organization.Id);
            _dbContext.WorkshopLocations.Add(workshopLocation);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (ownsTransaction)
                {
                    await transaction!.CommitAsync(cancellationToken);
                }
            }
            catch (DbUpdateException)
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return OwnerOnboardingResult.Failed(OwnerOnboardingFailureCategory.OrganizationCreationFailed);
            }

            return OwnerOnboardingResult.Succeeded(user.Id, organization.Id, workshopLocation.Id);
        }
        finally
        {
            if (ownsTransaction)
            {
                await transaction!.DisposeAsync();
            }
        }
    }
}
