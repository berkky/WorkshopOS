using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Reporting;
using WorkshopOS.Application.Team;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Models.Reporting;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.ReportingViewer)]
[Route("reports")]
public sealed class ReportsController : Controller
{
    private readonly IWorkshopReportingService _reportingService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IWebFailureMessages _messages;

    public ReportsController(
        IWorkshopReportingService reportingService,
        ITeamManagementService teamManagementService,
        IWebFailureMessages messages)
    {
        _reportingService = reportingService;
        _teamManagementService = teamManagementService;
        _messages = messages;
    }

    [HttpGet("operations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Operations(
        DateOnly? from,
        DateOnly? to,
        Guid? workshopLocationId)
    {
        Response.Headers.CacheControl = "private, no-store";

        var result = await _reportingService.GetOperationalReportAsync(new ReportingQuery
        {
            ActorUserId = GetActorUserId(),
            From = from,
            To = to,
            WorkshopLocationId = workshopLocationId,
        });

        if (!result.Success)
        {
            return MapFailure(result.FailureReason);
        }

        var report = result.Value!;
        return View(new OperationalReportViewModel
        {
            Filter = await BuildFilterAsync(from, to, workshopLocationId, report.Header),
            Report = report,
        });
    }

    [HttpGet("commercial")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Commercial(
        DateOnly? from,
        DateOnly? to,
        Guid? workshopLocationId)
    {
        Response.Headers.CacheControl = "private, no-store";

        var result = await _reportingService.GetCommercialReportAsync(new ReportingQuery
        {
            ActorUserId = GetActorUserId(),
            From = from,
            To = to,
            WorkshopLocationId = workshopLocationId,
        });

        if (!result.Success)
        {
            return MapFailure(result.FailureReason);
        }

        var report = result.Value!;
        return View(new CommercialReportViewModel
        {
            Filter = await BuildFilterAsync(from, to, workshopLocationId, report.Header),
            Report = report,
        });
    }

    private async Task<ReportingFilterViewModel> BuildFilterAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? workshopLocationId,
        ReportingContextHeader header)
    {
        return new ReportingFilterViewModel
        {
            From = header.From,
            To = header.To,
            WorkshopLocationId = workshopLocationId,
            LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync(),
        };
    }

    private IActionResult MapFailure(ReportingFailureReason? failureReason)
    {
        switch (failureReason)
        {
            case ReportingFailureReason.InvalidDateRange:
                return BadRequest(_messages.InvalidReportingDateRange());
            case ReportingFailureReason.LocationNotFound:
                return NotFound();
            case ReportingFailureReason.Unauthorized:
            case ReportingFailureReason.OrganizationNotResolved:
            default:
                return Forbid();
        }
    }

    private Guid GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}
