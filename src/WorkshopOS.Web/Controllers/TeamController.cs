using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Memberships;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Domain.Staff;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web.Models.Team;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("team")]
public sealed class TeamController : Controller
{
    private readonly ITeamManagementService _teamManagementService;
    private readonly IOrganizationMembershipManagementService _membershipManagementService;
    private readonly IAuthorizationService _authorizationService;

    public TeamController(
        ITeamManagementService teamManagementService,
        IOrganizationMembershipManagementService membershipManagementService,
        IAuthorizationService authorizationService)
    {
        _teamManagementService = teamManagementService;
        _membershipManagementService = membershipManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var staffMembers = await _teamManagementService.GetTeamListAsync();
        var memberships = await _teamManagementService.GetMembershipAccessListAsync();
        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.OrganizationManager)).Succeeded;

        return View(new TeamIndexViewModel
        {
            CanManageTeam = canManage,
            StaffMembers = staffMembers
                .Select(member => new TeamStaffRowViewModel
                {
                    StaffMemberId = member.StaffMemberId,
                    DisplayName = member.DisplayName,
                    Position = member.Position,
                    Status = member.Status,
                    LoginAccess = member.LoginAccess,
                    WorkshopLocations = member.WorkshopLocations,
                })
                .ToList(),
            Memberships = memberships
                .Select(member => new TeamMembershipRowViewModel
                {
                    MembershipId = member.MembershipId,
                    DisplayName = member.DisplayName,
                    Role = member.Role,
                    Status = member.Status,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        return View(await BuildStaffFormViewModelAsync());
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffMemberFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateStaffFormOptionsAsync(model);
            return View(model);
        }

        var result = await _teamManagementService.CreateStaffMemberAsync(new CreateStaffMemberCommand
        {
            DisplayName = model.DisplayName,
            Position = model.Position,
            JobTitle = model.JobTitle,
            ContactEmail = model.ContactEmail,
            PhoneNumber = model.PhoneNumber,
            LinkedUserId = model.LinkedUserId,
            WorkshopLocationIds = model.SelectedWorkshopLocationIds,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapTeamFailure(result.FailureReason));
            await PopulateStaffFormOptionsAsync(model);
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpGet("{staffId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid staffId)
    {
        var detail = await _teamManagementService.GetStaffMemberAsync(staffId);
        if (detail is null)
        {
            return NotFound();
        }

        var model = await BuildStaffFormViewModelAsync();
        model.StaffMemberId = detail.StaffMemberId;
        model.DisplayName = detail.DisplayName;
        model.Position = detail.Position;
        model.JobTitle = detail.JobTitle;
        model.ContactEmail = detail.ContactEmail;
        model.PhoneNumber = detail.PhoneNumber;
        model.LinkedUserId = detail.LinkedUserId;
        model.Status = detail.Status;
        model.SelectedWorkshopLocationIds = detail.WorkshopLocationIds.ToList();
        return View(model);
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpPost("{staffId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid staffId, StaffMemberFormViewModel model)
    {
        model.StaffMemberId = staffId;

        if (!ModelState.IsValid)
        {
            await PopulateStaffFormOptionsAsync(model);
            return View(model);
        }

        var result = await _teamManagementService.UpdateStaffMemberAsync(new UpdateStaffMemberCommand
        {
            StaffMemberId = staffId,
            DisplayName = model.DisplayName,
            Position = model.Position,
            JobTitle = model.JobTitle,
            ContactEmail = model.ContactEmail,
            PhoneNumber = model.PhoneNumber,
            LinkedUserId = model.LinkedUserId,
            Status = model.Status,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapTeamFailure(result.FailureReason));
            await PopulateStaffFormOptionsAsync(model);
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpPost("{staffId:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid staffId)
    {
        var result = await _teamManagementService.DeactivateStaffMemberAsync(staffId);
        if (!result.Success)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpPost("{staffId:guid}/locations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLocations(Guid staffId, SetStaffLocationsViewModel model)
    {
        model.StaffMemberId = staffId;

        var result = await _teamManagementService.SetStaffLocationsAsync(new SetStaffLocationsCommand
        {
            StaffMemberId = staffId,
            WorkshopLocationIds = model.WorkshopLocationIds,
        });

        if (!result.Success)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpPost("memberships/{membershipId:guid}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(Guid membershipId, ChangeMembershipRoleViewModel model)
    {
        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        if (!Enum.TryParse<OrganizationMembershipRole>(model.NewRole, out var newRole))
        {
            return Forbid();
        }

        var result = await _membershipManagementService.ChangeRoleAsync(new ChangeMembershipRoleCommand
        {
            ActorUserId = actorUserId.Value,
            MembershipId = membershipId,
            NewRole = newRole,
        });

        if (!result.Success)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.OrganizationManager)]
    [HttpPost("memberships/{membershipId:guid}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid membershipId, ChangeMembershipStatusViewModel model)
    {
        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        if (!Enum.TryParse<OrganizationMembershipStatus>(model.NewStatus, out var newStatus))
        {
            return Forbid();
        }

        var result = await _membershipManagementService.ChangeStatusAsync(new ChangeMembershipStatusCommand
        {
            ActorUserId = actorUserId.Value,
            MembershipId = membershipId,
            NewStatus = newStatus,
        });

        if (!result.Success)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<StaffMemberFormViewModel> BuildStaffFormViewModelAsync()
    {
        var model = new StaffMemberFormViewModel();
        await PopulateStaffFormOptionsAsync(model);
        return model;
    }

    private async Task PopulateStaffFormOptionsAsync(StaffMemberFormViewModel model)
    {
        model.LinkableUsers = (await _teamManagementService.GetLinkableMembershipsAsync())
            .Select(option => new LinkableUserOptionViewModel
            {
                UserId = option.UserId,
                DisplayLabel = option.DisplayLabel,
                MembershipRole = option.MembershipRole,
            })
            .ToList();

        model.WorkshopLocations = (await _teamManagementService.GetWorkshopLocationOptionsAsync())
            .Select(location => new WorkshopLocationOptionViewModel
            {
                WorkshopLocationId = location.WorkshopLocationId,
                Name = location.Name,
            })
            .ToList();
    }

    private Guid? GetCurrentUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private static string MapTeamFailure(TeamManagementFailureReason? reason) =>
        reason switch
        {
            TeamManagementFailureReason.LinkedUserNotMember =>
                "Selected login user is not a member of this organization.",
            TeamManagementFailureReason.LinkedUserAlreadyAssigned =>
                "Selected login user is already linked to another staff profile.",
            TeamManagementFailureReason.WorkshopLocationNotFound =>
                "One or more workshop locations are invalid for this organization.",
            TeamManagementFailureReason.InvalidInput =>
                "Please check the staff profile details and try again.",
            _ => "Unable to save staff profile.",
        };
}
