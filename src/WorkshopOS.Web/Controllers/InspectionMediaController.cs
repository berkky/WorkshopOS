using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.InspectionMedia;
using WorkshopOS.Infrastructure.Authorization;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("inspection-media")]
public sealed class InspectionMediaController : Controller
{
    private readonly IInspectionMediaService _inspectionMediaService;

    public InspectionMediaController(IInspectionMediaService inspectionMediaService)
    {
        _inspectionMediaService = inspectionMediaService;
    }

    [HttpGet("{mediaId:guid}/content")]
    public async Task<IActionResult> Content(Guid mediaId)
    {
        var content = await _inspectionMediaService.GetPhotoContentAsync(mediaId);
        if (content is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content.Content, content.ContentType);
    }
}
