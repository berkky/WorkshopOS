using System.ComponentModel.DataAnnotations;

namespace WorkshopOS.Web.Models.Organizations;

public sealed class OrganizationSelectionViewModel
{
    public IReadOnlyList<OrganizationSelectionItemViewModel> Organizations { get; set; } =
        Array.Empty<OrganizationSelectionItemViewModel>();

    public string? ReturnUrl { get; set; }
}

public sealed class OrganizationSelectionItemViewModel
{
    public Guid OrganizationId { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public string MembershipRole { get; set; } = string.Empty;
}

public sealed class OrganizationSelectPostViewModel
{
    [Required]
    public Guid OrganizationId { get; set; }

    public string? ReturnUrl { get; set; }
}
