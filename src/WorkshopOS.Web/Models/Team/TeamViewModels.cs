using System.ComponentModel.DataAnnotations;
using WorkshopOS.Web;
using WorkshopOS.Domain.Staff;

namespace WorkshopOS.Web.Models.Team;

public sealed class TeamIndexViewModel
{
    public IReadOnlyList<TeamStaffRowViewModel> StaffMembers { get; set; } =
        Array.Empty<TeamStaffRowViewModel>();

    public IReadOnlyList<TeamMembershipRowViewModel> Memberships { get; set; } =
        Array.Empty<TeamMembershipRowViewModel>();

    public bool CanManageTeam { get; set; }
}

public sealed class TeamStaffRowViewModel
{
    public Guid StaffMemberId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string LoginAccess { get; set; } = string.Empty;

    public IReadOnlyList<string> WorkshopLocations { get; set; } = Array.Empty<string>();
}

public sealed class TeamMembershipRowViewModel
{
    public Guid MembershipId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}

public sealed class StaffMemberFormViewModel
{
    public Guid? StaffMemberId { get; set; }

    [Required]
    [MaxLength(160)]
    [Display(Name = "Field_DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Field_Position")]
    public StaffPosition Position { get; set; }

    [MaxLength(120)]
    [Display(Name = "Field_JobTitle")]
    public string? JobTitle { get; set; }

    [EmailAddress]
    [MaxLength(256)]
    [Display(Name = "Field_ContactEmail")]
    public string? ContactEmail { get; set; }

    [MaxLength(50)]
    [Display(Name = "Field_PhoneNumber")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Field_LinkToExistingLogin")]
    public Guid? LinkedUserId { get; set; }

    [Display(Name = "Field_Status")]
    public StaffStatus Status { get; set; } = StaffStatus.Active;

    public IReadOnlyList<WorkshopLocationOptionViewModel> WorkshopLocations { get; set; } =
        Array.Empty<WorkshopLocationOptionViewModel>();

    public IReadOnlyList<LinkableUserOptionViewModel> LinkableUsers { get; set; } =
        Array.Empty<LinkableUserOptionViewModel>();

    public List<Guid> SelectedWorkshopLocationIds { get; set; } = new();
}

public sealed class WorkshopLocationOptionViewModel
{
    public Guid WorkshopLocationId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class LinkableUserOptionViewModel
{
    public Guid UserId { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public string MembershipRole { get; set; } = string.Empty;
}

public sealed class SetStaffLocationsViewModel
{
    [Required]
    public Guid StaffMemberId { get; set; }

    public List<Guid> WorkshopLocationIds { get; set; } = new();

    public IReadOnlyList<WorkshopLocationOptionViewModel> WorkshopLocations { get; set; } =
        Array.Empty<WorkshopLocationOptionViewModel>();
}

public sealed class ChangeMembershipRoleViewModel
{
    [Required]
    public Guid MembershipId { get; set; }

    [Required]
    public string NewRole { get; set; } = string.Empty;
}

public sealed class ChangeMembershipStatusViewModel
{
    [Required]
    public Guid MembershipId { get; set; }

    [Required]
    public string NewStatus { get; set; } = string.Empty;
}
