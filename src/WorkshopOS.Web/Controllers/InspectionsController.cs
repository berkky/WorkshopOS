using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.InspectionMedia;
using WorkshopOS.Application.Inspections;
using WorkshopOS.Application.Team;
using WorkshopOS.Domain.Inspections;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Models.Inspections;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
public sealed class InspectionsController : Controller
{
    private readonly IInspectionManagementService _inspectionManagementService;
    private readonly IInspectionMediaService _inspectionMediaService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly IWebFailureMessages _messages;

    public InspectionsController(
        IInspectionManagementService inspectionManagementService,
        IInspectionMediaService inspectionMediaService,
        ITeamManagementService teamManagementService,
        IWebFailureMessages messages)
    {
        _inspectionManagementService = inspectionManagementService;
        _inspectionMediaService = inspectionMediaService;
        _teamManagementService = teamManagementService;
        _messages = messages;
    }

    [HttpGet("/inspections")]
    public async Task<IActionResult> Index(
        string? search,
        InspectionStatus? status,
        Guid? workshopLocationId,
        int page = 1,
        int pageSize = InspectionListQuery.DefaultPageSize)
    {
        var result = await _inspectionManagementService.ListInspectionsAsync(new InspectionListQuery
        {
            Search = search,
            Status = status,
            WorkshopLocationId = workshopLocationId,
            Page = page,
            PageSize = pageSize,
        });

        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new InspectionListViewModel
        {
            Search = search,
            Status = status,
            WorkshopLocationId = workshopLocationId,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            LocationOptions = locationOptions,
            Inspections = result.Items
                .Select(inspection => new InspectionRowViewModel
                {
                    InspectionId = inspection.InspectionId,
                    RepairOrderId = inspection.RepairOrderId,
                    RepairOrderNumber = inspection.RepairOrderNumber,
                    Status = inspection.Status,
                    CustomerDisplayName = inspection.CustomerDisplayName,
                    VehicleSummary = inspection.VehicleSummary,
                    WorkshopLocationName = inspection.WorkshopLocationName,
                    TotalItems = inspection.TotalItems,
                    InspectedItems = inspection.InspectedItems,
                    CreatedAtUtc = inspection.CreatedAtUtc,
                    CompletedAtUtc = inspection.CompletedAtUtc,
                })
                .ToList(),
        });
    }

    [Authorize(Policy = PolicyNames.InspectionManager)]
    [HttpPost("/repair-orders/{repairOrderId:guid}/inspections")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid repairOrderId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _inspectionManagementService.CreateInspectionAsync(actorUserId.Value, repairOrderId);
        if (!result.Success)
        {
            if (result.FailureReason == InspectionOperationFailureReason.RepairOrderNotFound)
            {
                return NotFound();
            }

            if (result.FailureReason == InspectionOperationFailureReason.Unauthorized)
            {
                return Forbid();
            }

            TempData["InspectionError"] = _messages.Inspection(result.FailureReason);
            return RedirectToAction("Details", "RepairOrders", new { repairOrderId });
        }

        return RedirectToAction(nameof(Details), new { inspectionId = result.Value });
    }

    [HttpGet("/inspections/{inspectionId:guid}")]
    public async Task<IActionResult> Details(Guid inspectionId)
    {
        var actorUserId = GetActorUserId();
        var details = await _inspectionManagementService.GetInspectionDetailsAsync(
            inspectionId,
            actorUserId);

        if (details is null)
        {
            return NotFound();
        }

        return View(MapDetailsViewModel(details));
    }

    [HttpPost("/inspections/{inspectionId:guid}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid inspectionId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _inspectionManagementService.StartInspectionAsync(actorUserId.Value, inspectionId);
        if (!result.Success)
        {
            return MapMutationFailure(result, inspectionId);
        }

        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    [HttpPost("/inspections/{inspectionId:guid}/items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItems(Guid inspectionId, List<InspectionItemFormViewModel> editableItems)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        if (editableItems.Count == 0)
        {
            TempData["InspectionError"] = _messages.InspectionChecklistRequired();
            return RedirectToAction(nameof(Details), new { inspectionId });
        }

        var result = await _inspectionManagementService.UpdateInspectionItemsAsync(
            actorUserId.Value,
            new UpdateInspectionItemsCommand
            {
                InspectionId = inspectionId,
                Items = editableItems
                    .Select(item => new InspectionItemUpdate
                    {
                        InspectionItemId = item.InspectionItemId,
                        Condition = item.Condition,
                        Notes = item.Notes,
                    })
                    .ToList(),
            });

        if (!result.Success)
        {
            return MapMutationFailure(result, inspectionId);
        }

        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    [HttpPost("/inspections/{inspectionId:guid}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(Guid inspectionId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _inspectionManagementService.CompleteInspectionAsync(actorUserId.Value, inspectionId);
        if (!result.Success)
        {
            return MapMutationFailure(result, inspectionId);
        }

        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    [HttpPost("/inspections/{inspectionId:guid}/items/{inspectionItemId:guid}/photos")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(9 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(
        Guid inspectionId,
        Guid inspectionItemId,
        IFormFile? photo,
        string? caption)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        if (photo is null || photo.Length == 0)
        {
            TempData["InspectionError"] = _messages.InspectionPhotoRequired();
            return RedirectToAction(nameof(Details), new { inspectionId });
        }

        await using var stream = photo.OpenReadStream();
        var result = await _inspectionMediaService.UploadPhotoAsync(
            actorUserId.Value,
            new UploadInspectionPhotoCommand
            {
                InspectionId = inspectionId,
                InspectionItemId = inspectionItemId,
                Content = stream,
                DeclaredLength = photo.Length,
                DeclaredContentType = photo.ContentType,
                DeclaredFileName = photo.FileName,
                Caption = caption,
            });

        if (!result.Success)
        {
            return MapMediaMutationFailure(result.FailureReason, inspectionId);
        }

        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    [HttpPost("/inspections/{inspectionId:guid}/media/{mediaId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePhoto(Guid inspectionId, Guid mediaId)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId is null)
        {
            return Forbid();
        }

        var result = await _inspectionMediaService.RemovePhotoAsync(
            actorUserId.Value,
            new RemoveInspectionPhotoCommand
            {
                InspectionId = inspectionId,
                MediaId = mediaId,
            });

        if (!result.Success)
        {
            return MapMediaMutationFailure(result.FailureReason, inspectionId);
        }

        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    private IActionResult MapMediaMutationFailure(
        InspectionMediaOperationFailureReason? failureReason,
        Guid inspectionId)
    {
        if (failureReason is InspectionMediaOperationFailureReason.InspectionNotFound
            or InspectionMediaOperationFailureReason.MediaNotFound
            or InspectionMediaOperationFailureReason.InspectionItemNotFound)
        {
            return NotFound();
        }

        if (failureReason is InspectionMediaOperationFailureReason.Unauthorized
            or InspectionMediaOperationFailureReason.MembershipInactive
            or InspectionMediaOperationFailureReason.TechnicianProfileNotLinked
            or InspectionMediaOperationFailureReason.StaffInactive)
        {
            return Forbid();
        }

        TempData["InspectionError"] = _messages.InspectionMedia(failureReason);
        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    private IActionResult MapMutationFailure(InspectionOperationResult result, Guid inspectionId)
    {
        if (result.FailureReason == InspectionOperationFailureReason.InspectionNotFound)
        {
            return NotFound();
        }

        if (result.FailureReason is InspectionOperationFailureReason.Unauthorized
            or InspectionOperationFailureReason.MembershipInactive
            or InspectionOperationFailureReason.TechnicianProfileNotLinked
            or InspectionOperationFailureReason.StaffInactive)
        {
            return Forbid();
        }

        TempData["InspectionError"] = _messages.Inspection(result.FailureReason);
        return RedirectToAction(nameof(Details), new { inspectionId });
    }

    private static InspectionDetailsViewModel MapDetailsViewModel(InspectionDetails details)
    {
        var editableItems = details.CanUpdateItems
            ? details.Sections
                .SelectMany(section => section.Items)
                .OrderBy(item => item.SortOrder)
                .Select(item => new InspectionItemFormViewModel
                {
                    InspectionItemId = item.InspectionItemId,
                    Condition = item.Condition,
                    Notes = item.Notes,
                })
                .ToList()
            : new List<InspectionItemFormViewModel>();

        return new InspectionDetailsViewModel
        {
            InspectionId = details.InspectionId,
            RepairOrderId = details.RepairOrderId,
            RepairOrderNumber = details.RepairOrderNumber,
            Status = details.Status,
            CustomerDisplayName = details.CustomerDisplayName,
            VehicleSummary = details.VehicleSummary,
            WorkshopLocationName = details.WorkshopLocationName,
            Sections = details.Sections
                .Select(section => new InspectionSectionViewModel
                {
                    Section = section.Section,
                    Items = section.Items
                        .Select(item => new InspectionItemDisplayViewModel
                        {
                            InspectionItemId = item.InspectionItemId,
                            Name = item.Name,
                            Condition = item.Condition,
                            Notes = item.Notes,
                            SortOrder = item.SortOrder,
                            Media = item.Media
                                .Select(media => new InspectionMediaDisplayViewModel
                                {
                                    MediaId = media.MediaId,
                                    ContentType = media.ContentType,
                                    LengthBytes = media.LengthBytes,
                                    Caption = media.Caption,
                                    UploadedAtUtc = media.UploadedAtUtc,
                                })
                                .ToList(),
                        })
                        .ToList(),
                })
                .ToList(),
            Summary = new InspectionSummaryViewModel
            {
                Total = details.Summary.Total,
                Inspected = details.Summary.Inspected,
                Good = details.Summary.Good,
                Attention = details.Summary.Attention,
                Critical = details.Summary.Critical,
                Monitor = details.Summary.Monitor,
            },
            StartedAtUtc = details.StartedAtUtc,
            CompletedAtUtc = details.CompletedAtUtc,
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            CanStart = details.CanStart,
            CanUpdateItems = details.CanUpdateItems,
            CanComplete = details.CanComplete,
            CanUploadMedia = details.CanUploadMedia,
            CanRemoveMedia = details.CanRemoveMedia,
            ActivePhotoCount = details.ActivePhotoCount,
            EditableItems = editableItems,
        };
    }


    private Guid? GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

}
