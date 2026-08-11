using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Operations;
using WorkshopOS.Application.RepairOrders;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.RepairOrders;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Models.Operations;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("operations")]
public sealed class OperationsController : Controller
{
    private readonly IWorkshopOperationsService _workshopOperationsService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IWebFailureMessages _messages;

    public OperationsController(
        IWorkshopOperationsService workshopOperationsService,
        ITeamManagementService teamManagementService,
        IAuthorizationService authorizationService,
        IWebFailureMessages messages)
    {
        _workshopOperationsService = workshopOperationsService;
        _teamManagementService = teamManagementService;
        _authorizationService = authorizationService;
        _messages = messages;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid? workshopLocationId,
        RepairOrderStatus? status,
        RepairOrderPriority? priority,
        Guid? assignedStaffMemberId,
        bool unassignedOnly = false,
        string? search = null)
    {
        var result = await _workshopOperationsService.GetOperationsBoardAsync(new OperationsBoardQuery
        {
            WorkshopLocationId = workshopLocationId,
            Status = status,
            Priority = priority,
            AssignedStaffMemberId = assignedStaffMemberId,
            UnassignedOnly = unassignedOnly,
            Search = search,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.RepairOrderManager)).Succeeded;
        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
        var technicianOptions = await BuildTechnicianFilterOptionsAsync();

        var items = result.Items.Select(MapBoardItem).ToList();

        return View(new OperationsBoardViewModel
        {
            Summary = new OperationsBoardSummaryViewModel
            {
                ActiveJobs = result.Summary.ActiveJobs,
                Unassigned = result.Summary.Unassigned,
                InProgress = result.Summary.InProgress,
                Urgent = result.Summary.Urgent,
            },
            IntakeItems = items.Where(item => item.Status == RepairOrderStatus.Draft).ToList(),
            InProgressItems = items.Where(item => item.Status == RepairOrderStatus.InProgress).ToList(),
            OtherActiveItems = items.Where(item =>
                item.Status != RepairOrderStatus.Draft && item.Status != RepairOrderStatus.InProgress).ToList(),
            WorkshopLocationId = workshopLocationId,
            Status = status,
            Priority = priority,
            AssignedStaffMemberId = assignedStaffMemberId,
            UnassignedOnly = unassignedOnly,
            Search = search,
            TotalMatchingCount = result.TotalMatchingCount,
            ResultsTruncated = result.ResultsTruncated,
            CanManageOperations = canManage,
            LocationOptions = locationOptions,
            TechnicianOptions = technicianOptions,
        });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("repair-orders/{repairOrderId:guid}/assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid repairOrderId, Guid staffMemberId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.AssignTechnicianAsync(
            actorUserId.Value,
            new AssignTechnicianCommand
            {
                RepairOrderId = repairOrderId,
                StaffMemberId = staffMemberId,
            });

        if (!result.Success)
        {
            return MapOperationFailure(result, repairOrderId);
        }

        return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("repair-orders/{repairOrderId:guid}/reassign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(Guid repairOrderId, Guid newStaffMemberId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.ReassignTechnicianAsync(
            actorUserId.Value,
            new ReassignTechnicianCommand
            {
                RepairOrderId = repairOrderId,
                NewStaffMemberId = newStaffMemberId,
            });

        if (!result.Success)
        {
            return MapOperationFailure(result, repairOrderId);
        }

        return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("repair-orders/{repairOrderId:guid}/unassign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.UnassignTechnicianAsync(
            actorUserId.Value,
            new UnassignTechnicianCommand { RepairOrderId = repairOrderId });

        if (!result.Success)
        {
            return MapOperationFailure(result, repairOrderId);
        }

        return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
    }

    [Authorize(Policy = PolicyNames.RepairOrderManager)]
    [HttpPost("repair-orders/{repairOrderId:guid}/priority")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePriority(Guid repairOrderId, RepairOrderPriority priority)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _workshopOperationsService.ChangeRepairOrderPriorityAsync(
            actorUserId.Value,
            new ChangeRepairOrderPriorityCommand
            {
                RepairOrderId = repairOrderId,
                Priority = priority,
            });

        if (!result.Success)
        {
            return MapOperationFailure(result, repairOrderId);
        }

        return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
    }

    private async Task<IReadOnlyList<TechnicianFilterOptionViewModel>> BuildTechnicianFilterOptionsAsync()
    {
        var team = await _teamManagementService.GetTeamListAsync();
        return team
            .Where(member => member.Position == "Technician" && member.Status == "Active")
            .Select(member => new TechnicianFilterOptionViewModel
            {
                StaffMemberId = member.StaffMemberId,
                DisplayName = member.DisplayName,
            })
            .ToList();
    }

    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private IActionResult MapOperationFailure(WorkshopOperationResult result, Guid repairOrderId)
    {
        if (result.FailureReason == WorkshopOperationFailureReason.RepairOrderNotFound)
        {
            return NotFound();
        }

        if (result.FailureReason == WorkshopOperationFailureReason.Unauthorized)
        {
            return Forbid();
        }

        TempData["OperationsError"] = _messages.Operations(result.FailureReason);
        return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
    }

    private static OperationsBoardItemViewModel MapBoardItem(OperationsBoardItem item) =>
        new()
        {
            RepairOrderId = item.RepairOrderId,
            Number = item.Number,
            Status = item.Status,
            Priority = item.Priority,
            CustomerDisplayName = item.CustomerDisplayName,
            VehicleSummary = item.VehicleSummary,
            WorkshopLocationName = item.WorkshopLocationName,
            OpenedAtUtc = item.OpenedAtUtc,
            AssignedTechnicianDisplayName = item.AssignedTechnicianDisplayName,
            AssignedTechnicianIsInactive = item.AssignedTechnicianIsInactive,
            TechnicianWorkStatus = item.TechnicianWorkStatus,
            LatestInspectionStatus = item.LatestInspectionStatus,
            LatestEstimateStatus = item.LatestEstimateStatus,
        };

}
