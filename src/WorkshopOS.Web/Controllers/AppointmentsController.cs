using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkshopOS.Application.Appointments;
using WorkshopOS.Application.Customers;
using WorkshopOS.Application.Team;
using WorkshopOS.Application.Vehicles;
using WorkshopOS.Domain.Appointments;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Web.Models.Appointments;

namespace WorkshopOS.Web.Controllers;

[Authorize(Policy = PolicyNames.OrganizationMember)]
[Route("appointments")]
public sealed class AppointmentsController : Controller
{
    private readonly IAppointmentManagementService _appointmentManagementService;
    private readonly ITeamManagementService _teamManagementService;
    private readonly ICustomerManagementService _customerManagementService;
    private readonly IVehicleManagementService _vehicleManagementService;
    private readonly IAuthorizationService _authorizationService;

    public AppointmentsController(
        IAppointmentManagementService appointmentManagementService,
        ITeamManagementService teamManagementService,
        ICustomerManagementService customerManagementService,
        IVehicleManagementService vehicleManagementService,
        IAuthorizationService authorizationService)
    {
        _appointmentManagementService = appointmentManagementService;
        _teamManagementService = teamManagementService;
        _customerManagementService = customerManagementService;
        _vehicleManagementService = vehicleManagementService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        Guid? workshopLocationId,
        Guid? customerId,
        Guid? vehicleId,
        AppointmentStatus? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int page = 1,
        int pageSize = AppointmentListQuery.DefaultPageSize)
    {
        var result = await _appointmentManagementService.ListAppointmentsAsync(new AppointmentListQuery
        {
            WorkshopLocationId = workshopLocationId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            Status = status,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Page = page,
            PageSize = pageSize,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.AppointmentManager)).Succeeded;
        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();

        return View(new AppointmentListViewModel
        {
            WorkshopLocationId = workshopLocationId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            Status = status,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            CanManageAppointments = canManage,
            LocationOptions = locationOptions,
            Appointments = await MapListRowsAsync(result.Items),
        });
    }

    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar(
        DateOnly? weekStart,
        Guid? workshopLocationId,
        AppointmentStatus? status)
    {
        var referenceWeek = weekStart ?? DateOnly.FromDateTime(DateTime.Today);
        var result = await _appointmentManagementService.GetCalendarAppointmentsAsync(new AppointmentCalendarQuery
        {
            WeekStartLocal = referenceWeek,
            WorkshopLocationId = workshopLocationId,
            Status = status,
        });

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.AppointmentManager)).Succeeded;
        var locationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
        var days = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var date = result.WeekStartLocal.AddDays(offset);
                return new AppointmentCalendarDayViewModel
                {
                    Date = date,
                    Appointments = result.Items
                        .Where(item => DateOnly.FromDateTime(item.ScheduledStartLocal.DateTime) == date)
                        .Select(item => new AppointmentCalendarEntryViewModel
                        {
                            AppointmentId = item.AppointmentId,
                            ScheduledStartLocal = item.ScheduledStartLocal,
                            ScheduledEndLocal = item.ScheduledEndLocal,
                            Status = item.Status,
                            WorkshopLocationName = item.WorkshopLocationName,
                            CustomerDisplayName = item.CustomerDisplayName,
                            VehicleSummary = item.VehicleSummary,
                        })
                        .ToList(),
                };
            })
            .ToList();

        return View(new AppointmentCalendarViewModel
        {
            WeekStartLocal = result.WeekStartLocal,
            WeekEndLocal = result.WeekStartLocal.AddDays(6),
            TimeZoneId = result.TimeZoneId,
            WorkshopLocationId = workshopLocationId,
            Status = status,
            CanManageAppointments = canManage,
            LocationOptions = locationOptions,
            Days = days,
        });
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpGet("create")]
    public async Task<IActionResult> Create(
        Guid? customerId,
        Guid? vehicleId,
        Guid? workshopLocationId)
    {
        return View(await BuildFormViewModelAsync(new AppointmentFormViewModel
        {
            CustomerId = customerId ?? Guid.Empty,
            VehicleId = vehicleId ?? Guid.Empty,
            WorkshopLocationId = workshopLocationId ?? Guid.Empty,
        }));
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AppointmentFormViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormViewModelAsync(viewModel));
        }

        var schedule = await ConvertScheduleAsync(
            viewModel.WorkshopLocationId,
            viewModel.LocalDate,
            viewModel.StartTime,
            viewModel.EndTime);
        if (!schedule.Success)
        {
            ModelState.AddModelError(string.Empty, schedule.ErrorMessage);
            return View(await BuildFormViewModelAsync(viewModel));
        }

        var result = await _appointmentManagementService.CreateAppointmentAsync(new CreateAppointmentCommand
        {
            WorkshopLocationId = viewModel.WorkshopLocationId,
            CustomerId = viewModel.CustomerId,
            VehicleId = viewModel.VehicleId,
            ScheduledStartUtc = schedule.StartUtc,
            ScheduledEndUtc = schedule.EndUtc,
            CustomerConcern = viewModel.CustomerConcern,
            InternalNotes = viewModel.InternalNotes,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(await BuildFormViewModelAsync(viewModel));
        }

        return RedirectToAction(nameof(Details), new { appointmentId = result.Value });
    }

    [HttpGet("{appointmentId:guid}")]
    public async Task<IActionResult> Details(Guid appointmentId)
    {
        var details = await _appointmentManagementService.GetAppointmentDetailsAsync(appointmentId);
        if (details is null)
        {
            return NotFound();
        }

        var canManage = (await _authorizationService.AuthorizeAsync(User, PolicyNames.AppointmentManager)).Succeeded;
        var canManageRepairOrders =
            (await _authorizationService.AuthorizeAsync(User, PolicyNames.RepairOrderManager)).Succeeded;

        return View(new AppointmentDetailsViewModel
        {
            AppointmentId = details.AppointmentId,
            WorkshopLocationName = details.WorkshopLocationName,
            CustomerId = details.CustomerId,
            CustomerDisplayName = details.CustomerDisplayName,
            VehicleId = details.VehicleId,
            VehicleSummary = details.VehicleSummary,
            Status = details.Status,
            ScheduledStartLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(
                details.ScheduledStartUtc,
                details.TimeZoneId),
            ScheduledEndLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(
                details.ScheduledEndUtc,
                details.TimeZoneId),
            TimeZoneId = details.TimeZoneId,
            CustomerConcern = details.CustomerConcern,
            InternalNotes = details.InternalNotes,
            CreatedAtUtc = details.CreatedAtUtc,
            UpdatedAtUtc = details.UpdatedAtUtc,
            CanManageAppointments = canManage,
            LinkedRepairOrderId = details.LinkedRepairOrderId,
            CanManageRepairOrders = canManageRepairOrders,
        });
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpGet("{appointmentId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid appointmentId)
    {
        var details = await _appointmentManagementService.GetAppointmentDetailsAsync(appointmentId);
        if (details is null)
        {
            return NotFound();
        }

        if (details.Status == AppointmentStatus.Cancelled)
        {
            return RedirectToAction(nameof(Details), new { appointmentId });
        }

        return View(await BuildFormViewModelAsync(new AppointmentFormViewModel
        {
            AppointmentId = details.AppointmentId,
            WorkshopLocationId = details.WorkshopLocationId,
            CustomerId = details.CustomerId,
            VehicleId = details.VehicleId,
            CustomerConcern = details.CustomerConcern,
            InternalNotes = details.InternalNotes,
        }));
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpPost("{appointmentId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid appointmentId, AppointmentFormViewModel viewModel)
    {
        viewModel.AppointmentId = appointmentId;

        if (!ModelState.IsValid)
        {
            return View(await BuildFormViewModelAsync(viewModel));
        }

        var result = await _appointmentManagementService.UpdateAppointmentAsync(new UpdateAppointmentCommand
        {
            AppointmentId = appointmentId,
            WorkshopLocationId = viewModel.WorkshopLocationId,
            CustomerId = viewModel.CustomerId,
            VehicleId = viewModel.VehicleId,
            CustomerConcern = viewModel.CustomerConcern,
            InternalNotes = viewModel.InternalNotes,
        });

        if (!result.Success)
        {
            if (result.FailureReason == AppointmentOperationFailureReason.AppointmentNotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            return View(await BuildFormViewModelAsync(viewModel));
        }

        return RedirectToAction(nameof(Details), new { appointmentId });
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpGet("{appointmentId:guid}/reschedule")]
    public async Task<IActionResult> Reschedule(Guid appointmentId)
    {
        var details = await _appointmentManagementService.GetAppointmentDetailsAsync(appointmentId);
        if (details is null)
        {
            return NotFound();
        }

        if (details.Status == AppointmentStatus.Cancelled)
        {
            return RedirectToAction(nameof(Details), new { appointmentId });
        }

        var startLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(details.ScheduledStartUtc, details.TimeZoneId);
        var endLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(details.ScheduledEndUtc, details.TimeZoneId);

        return View(new AppointmentRescheduleViewModel
        {
            AppointmentId = details.AppointmentId,
            Summary = $"{details.CustomerDisplayName} — {details.VehicleSummary}",
            WorkshopLocationId = details.WorkshopLocationId,
            LocalDate = DateOnly.FromDateTime(startLocal.DateTime),
            StartTime = TimeOnly.FromDateTime(startLocal.DateTime),
            EndTime = TimeOnly.FromDateTime(endLocal.DateTime),
            LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync(),
        });
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpPost("{appointmentId:guid}/reschedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(Guid appointmentId, AppointmentRescheduleViewModel viewModel)
    {
        viewModel.AppointmentId = appointmentId;

        if (!ModelState.IsValid)
        {
            viewModel.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
            return View(viewModel);
        }

        var schedule = await ConvertScheduleAsync(
            viewModel.WorkshopLocationId,
            viewModel.LocalDate,
            viewModel.StartTime,
            viewModel.EndTime);
        if (!schedule.Success)
        {
            ModelState.AddModelError(string.Empty, schedule.ErrorMessage);
            viewModel.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
            return View(viewModel);
        }

        var result = await _appointmentManagementService.RescheduleAppointmentAsync(new RescheduleAppointmentCommand
        {
            AppointmentId = appointmentId,
            WorkshopLocationId = viewModel.WorkshopLocationId,
            ScheduledStartUtc = schedule.StartUtc,
            ScheduledEndUtc = schedule.EndUtc,
        });

        if (!result.Success)
        {
            if (result.FailureReason == AppointmentOperationFailureReason.AppointmentNotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, MapFailure(result.FailureReason));
            viewModel.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
            return View(viewModel);
        }

        return RedirectToAction(nameof(Details), new { appointmentId });
    }

    [Authorize(Policy = PolicyNames.AppointmentManager)]
    [HttpPost("{appointmentId:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid appointmentId)
    {
        var result = await _appointmentManagementService.CancelAppointmentAsync(appointmentId);
        if (!result.Success)
        {
            return result.FailureReason == AppointmentOperationFailureReason.AppointmentNotFound
                ? NotFound()
                : Forbid();
        }

        return RedirectToAction(nameof(Details), new { appointmentId });
    }

    private async Task<AppointmentFormViewModel> BuildFormViewModelAsync(AppointmentFormViewModel viewModel)
    {
        viewModel.LocationOptions = await _teamManagementService.GetWorkshopLocationOptionsAsync();
        viewModel.CustomerOptions = (await _customerManagementService.ListCustomersAsync(new CustomerListQuery
        {
            PageSize = CustomerListQuery.MaxPageSize,
        })).Items
            .Select(customer => new CustomerOptionViewModel
            {
                CustomerId = customer.CustomerId,
                DisplayName = customer.DisplayName,
            })
            .ToList();

        viewModel.VehicleOptions = (await _vehicleManagementService.ListVehiclesAsync(new VehicleListQuery
        {
            PageSize = VehicleListQuery.MaxPageSize,
        })).Items
            .Select(vehicle => new VehicleOptionViewModel
            {
                VehicleId = vehicle.VehicleId,
                Summary = $"{vehicle.Make} {vehicle.Model}",
            })
            .ToList();

        return viewModel;
    }

    private async Task<List<AppointmentRowViewModel>> MapListRowsAsync(
        IReadOnlyList<AppointmentListItem> items)
    {
        var rows = new List<AppointmentRowViewModel>(items.Count);
        foreach (var item in items)
        {
            var timeZoneId = await _appointmentManagementService.ResolveSchedulingTimeZoneAsync(item.WorkshopLocationId)
                ?? "UTC";

            rows.Add(new AppointmentRowViewModel
            {
                AppointmentId = item.AppointmentId,
                ScheduledStartUtc = item.ScheduledStartUtc,
                ScheduledEndUtc = item.ScheduledEndUtc,
                Status = item.Status,
                WorkshopLocationName = item.WorkshopLocationName,
                CustomerDisplayName = item.CustomerDisplayName,
                VehicleSummary = item.VehicleSummary,
                TimeZoneId = timeZoneId,
                ScheduledStartLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(
                    item.ScheduledStartUtc,
                    timeZoneId),
                ScheduledEndLocal = AppointmentSchedulingConverter.ConvertUtcToLocal(
                    item.ScheduledEndUtc,
                    timeZoneId),
            });
        }

        return rows;
    }

    private async Task<ScheduleConversionResult> ConvertScheduleAsync(
        Guid workshopLocationId,
        DateOnly localDate,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        var timeZoneId = await _appointmentManagementService.ResolveSchedulingTimeZoneAsync(workshopLocationId);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return ScheduleConversionResult.Failed("The selected workshop location is not available.");
        }

        var startLocal = localDate.ToDateTime(startTime);
        var endLocal = localDate.ToDateTime(endTime);

        if (!AppointmentSchedulingConverter.TryConvertLocalToUtc(timeZoneId, startLocal, out var startUtc, out var startError))
        {
            return ScheduleConversionResult.Failed(startError ?? "Unable to convert the start time.");
        }

        if (!AppointmentSchedulingConverter.TryConvertLocalToUtc(timeZoneId, endLocal, out var endUtc, out var endError))
        {
            return ScheduleConversionResult.Failed(endError ?? "Unable to convert the end time.");
        }

        return ScheduleConversionResult.Succeeded(startUtc, endUtc);
    }

    private readonly record struct ScheduleConversionResult(
        bool Success,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string ErrorMessage)
    {
        public static ScheduleConversionResult Succeeded(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
            new(true, startUtc, endUtc, string.Empty);

        public static ScheduleConversionResult Failed(string errorMessage) =>
            new(false, default, default, errorMessage);
    }

    private static string MapFailure(AppointmentOperationFailureReason? reason) =>
        reason switch
        {
            AppointmentOperationFailureReason.InvalidInput =>
                "Please check the appointment schedule and try again.",
            AppointmentOperationFailureReason.WorkshopLocationNotFound =>
                "The selected workshop location was not found.",
            AppointmentOperationFailureReason.CustomerNotFound =>
                "The selected customer was not found in the current organization.",
            AppointmentOperationFailureReason.VehicleNotFound =>
                "The selected vehicle was not found in the current organization.",
            AppointmentOperationFailureReason.CustomerVehicleMismatch =>
                "The selected vehicle is not currently associated with the selected customer.",
            AppointmentOperationFailureReason.PastStartNotAllowed =>
                "New appointments cannot be scheduled in the past.",
            AppointmentOperationFailureReason.VehicleOverlap =>
                "This vehicle already has an overlapping appointment.",
            AppointmentOperationFailureReason.ConcurrencyConflict =>
                "Another booking was saved at the same time. Please review the schedule and try again.",
            AppointmentOperationFailureReason.CannotModifyCancelled =>
                "Cancelled appointments cannot be modified.",
            AppointmentOperationFailureReason.OrganizationUnresolved =>
                "Organization context is not available.",
            _ => "Unable to save appointment.",
        };
}
