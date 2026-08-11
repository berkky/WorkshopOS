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
public sealed class DashboardController : Controller
{
    private readonly IWorkshopReportingService _reportingService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IWebFailureMessages _messages;

    public DashboardController(
        IWorkshopReportingService reportingService,
        ITeamManagementService teamManagementService,
        IWebFailureMessages messages)
    {
        _reportingService = reportingService;
        _teamManagementService = teamManagementService;
        _messages = messages;
    }

    [HttpGet("/dashboard")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(
        DateOnly? from,
        DateOnly? to,
        Guid? workshopLocationId)
    {
        Response.Headers.CacheControl = "private, no-store";

        var actorUserId = GetActorUserId();
        var result = await _reportingService.GetExecutiveDashboardAsync(new ReportingQuery
        {
            ActorUserId = actorUserId,
            From = from,
            To = to,
            WorkshopLocationId = workshopLocationId,
        });

        if (!result.Success)
        {
            return MapFailure(result.FailureReason);
        }

        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
        var dashboard = result.Value!;

        return View(new DashboardViewModel
        {
            Filter = new ReportingFilterViewModel
            {
                From = dashboard.Header.From,
                To = dashboard.Header.To,
                WorkshopLocationId = workshopLocationId,
                LocationOptions = locationOptions,
            },
            Header = dashboard.Header,
            CurrentSnapshot = dashboard.CurrentSnapshot,
            SelectedPeriod = dashboard.SelectedPeriod,
        });
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
