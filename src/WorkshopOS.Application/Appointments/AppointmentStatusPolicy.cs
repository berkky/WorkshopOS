using WorkshopOS.Domain.Appointments;

namespace WorkshopOS.Application.Appointments;

public static class AppointmentStatusPolicy
{
    public static bool ReservesVehicleTime(AppointmentStatus status) =>
        status is AppointmentStatus.Scheduled
            or AppointmentStatus.Confirmed
            or AppointmentStatus.CheckedIn;

    public static bool CanBeCancelled(AppointmentStatus status) =>
        status is not AppointmentStatus.Cancelled;
}
