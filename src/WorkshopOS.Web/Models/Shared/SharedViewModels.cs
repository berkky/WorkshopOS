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
