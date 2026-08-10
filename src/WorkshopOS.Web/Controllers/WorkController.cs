using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Operations;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Work;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("work")]
public sealed class WorkController : Controller
{
    private readonly IWorkshopOperationsService _workshopOperationsService;

    public WorkController(IWorkshopOperationsService workshopOperationsService)
    {
        _workshopOperationsService = workshopOperationsService;
    }

    [HttpGet("my-jobs")]
    public async Task<IActionResult> MyJobs()
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.GetMyWorkAsync(actorUserId.Value);

        return View(new MyWorkViewModel
        {
            HasLinkedTechnicianProfile = result.HasLinkedTechnicianProfile,
            Items = result.Items
                .Select(item => new MyWorkItemViewModel
                {
                    RepairOrderId = item.RepairOrderId,
                    Number = item.Number,
                    Priority = item.Priority,
                    RepairOrderStatus = item.RepairOrderStatus,
                    TechnicianWorkStatus = item.TechnicianWorkStatus,
                    CustomerDisplayName = item.CustomerDisplayName,
                    VehicleSummary = item.VehicleSummary,
                    WorkshopLocationName = item.WorkshopLocationName,
                    CanStartWork = item.CanStartWork,
                    CanCompleteWork = item.CanCompleteWork,
                    LatestInspectionId = item.LatestInspectionId,
                    LatestInspectionStatus = item.LatestInspectionStatus,
                    LatestEstimateStatus = item.LatestEstimateStatus,
                })
                .ToList(),
        });
    }

    [HttpPost("repair-orders/{repairOrderId:guid}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.StartAssignedWorkAsync(actorUserId.Value, repairOrderId);
        if (!result.Success)
        {
            return result.FailureReason == WorkshopOperationFailureReason.RepairOrderNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(MyJobs));
    }

    [HttpPost("repair-orders/{repairOrderId:guid}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.CompleteAssignedWorkAsync(actorUserId.Value, repairOrderId);
        if (!result.Success)
        {
            return result.FailureReason == WorkshopOperationFailureReason.RepairOrderNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(MyJobs));
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
