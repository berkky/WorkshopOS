using Microsoft.AspNetCore.Html;

namespace WorkshopOS.Web.Models.Shared;

public sealed class PageHeaderViewModel
{
    public string? Eyebrow { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public IHtmlContent? Actions { get; init; }
}

public sealed class EmptyStateViewModel
{
    public required string Title { get; init; }

    public string? Description { get; init; }

    public IHtmlContent? Action { get; init; }
}

public sealed class DetailWorkspaceHeaderViewModel
{
    public string? Eyebrow { get; init; }

    public required string Title { get; init; }

    public IHtmlContent? Meta { get; init; }

    public IHtmlContent? StatusBadges { get; init; }

    public IHtmlContent? Actions { get; init; }
}

public sealed class PaginationViewModel
{
    public required string Action { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required int TotalCount { get; init; }

    public IDictionary<string, string> RouteValues { get; init; } =
        new Dictionary<string, string>();
}

public sealed class RelatedModuleViewModel
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string PrimaryLinkText { get; init; }

    public required string PrimaryLinkUrl { get; init; }

    public string? SecondaryLinkText { get; init; }

    public string? SecondaryLinkUrl { get; init; }
}
