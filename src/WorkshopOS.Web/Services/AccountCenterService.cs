using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Application.Organizations;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web.Presentation;

namespace WorkshopOS.Web.Services;

public sealed record AccountCenterSnapshot(
    string Email,
    string? DisplayName,
    string? PhoneNumber,
    string Initials,
    string? OrganizationName,
    string? MembershipRoleLabel,
    string? MembershipStatusLabel,
    string? OrganizationTimeZoneId,
    bool CanSwitchWorkspace);

public interface IAccountCenterService
{
    Task<AccountCenterSnapshot?> GetSnapshotAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

public sealed class AccountCenterService : IAccountCenterService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly IOrganizationSelectionService _organizationSelectionService;
    private readonly IWorkshopUiFormatting _formatting;

    public AccountCenterService(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        IOrganizationSelectionService organizationSelectionService,
        IWorkshopUiFormatting formatting)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _organizationSelectionService = organizationSelectionService;
        _formatting = formatting;
    }

    public async Task<AccountCenterSnapshot?> GetSnapshotAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user is null)
        {
            if (principal.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var identityEmail = principal.Identity.Name ?? string.Empty;
            return new AccountCenterSnapshot(
                identityEmail,
                null,
                null,
                BuildInitialsFromEmail(identityEmail),
                null,
                null,
                null,
                null,
                false);
        }

        string? organizationName = null;
        string? roleLabel = null;
        string? statusLabel = null;
        string? timeZoneId = null;

        if (_organizationContext.IsResolved && _organizationContext.OrganizationId.HasValue)
        {
            var membership = await (
                from member in _dbContext.OrganizationMemberships.AsNoTracking()
                join organization in _dbContext.Organizations.AsNoTracking()
                    on member.OrganizationId equals organization.Id
                where member.UserId == user.Id
                      && member.OrganizationId == _organizationContext.OrganizationId.Value
                select new
                {
                    organization.Name,
                    organization.TimeZoneId,
                    member.Role,
                    member.Status,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (membership is not null)
            {
                organizationName = membership.Name;
                timeZoneId = membership.TimeZoneId;
                roleLabel = _formatting.FormatMembershipRole(membership.Role);
                statusLabel = _formatting.FormatMembershipStatus(membership.Status);
            }
        }

        var selectableOrganizations = await _organizationSelectionService
            .GetSelectableOrganizationsAsync(user.Id, cancellationToken);

        return new AccountCenterSnapshot(
            user.Email ?? string.Empty,
            user.DisplayName,
            user.PhoneNumber,
            BuildInitials(user),
            organizationName,
            roleLabel,
            statusLabel,
            timeZoneId,
            selectableOrganizations.Count > 1);
    }

    private static string BuildInitials(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            return BuildInitialsFromDisplayName(user.DisplayName);
        }

        return BuildInitialsFromEmail(user.Email ?? "U");
    }

    private static string BuildInitialsFromDisplayName(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }

        return char.ToUpperInvariant(parts[0][0]).ToString();
    }

    private static string BuildInitialsFromEmail(string email) =>
        string.IsNullOrWhiteSpace(email) ? "U" : char.ToUpperInvariant(email[0]).ToString();
}
