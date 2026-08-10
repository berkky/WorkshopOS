# WorkshopOS Appointment Scheduling v1

## Appointment != RepairOrder

```text
Appointment  = scheduled customer/vehicle visit (pre-service intent)
RepairOrder  = actual workshop job/work execution
```

STEP 10 does not create RepairOrders from appointments or merge these lifecycles.

## Tenant ownership

`Appointment.OrganizationId` is server-controlled from resolved `IOrganizationContext`.

Client commands and forms must not supply:

```text
Id
OrganizationId
CreatedAtUtc
UpdatedAtUtc
```

## Same-tenant relationships

Appointment references use composite foreign keys:

```text
(OrganizationId, WorkshopLocationId) → WorkshopLocation
(OrganizationId, CustomerId)       → Customer
(OrganizationId, VehicleId)        → Vehicle
```

Cross-tenant references are rejected by service validation and PostgreSQL constraints.

## Customer / vehicle consistency

When both `CustomerId` and `VehicleId` are present:

- each must belong to the current organization
- if `Vehicle.CurrentCustomerId` is set, it must match the appointment customer at scheduling time
- appointments do not reassign vehicle ownership

## UTC persistence and timezone display

Appointment times are stored as `DateTimeOffset` UTC.

UI forms accept local workshop time. Effective timezone:

```text
WorkshopLocation.TimeZoneId
or Organization.TimeZoneId
```

Invalid DST local times are rejected. Ambiguous DST times resolve to the earlier offset occurrence.

## Vehicle overlap invariant

The same vehicle cannot have overlapping **active** appointments:

```text
Scheduled
Confirmed
CheckedIn
```

Overlap rule:

```text
existing.Start < requested.End AND existing.End > requested.Start
```

Cancelled / completed / no-show appointments do not reserve vehicle time.

Create and reschedule operations use PostgreSQL `Serializable` transactions for overlap-sensitive writes.

## Cancellation lifecycle

Cancellation uses `AppointmentStatus.Cancelled`. No hard delete.

Cancelled appointments remain historical records and stop blocking vehicle time.

## AppointmentManager authorization

| Role | Appointment writes |
|---|---|
| Owner | Allowed |
| Administrator | Allowed |
| ServiceAdvisor | Allowed |
| Technician | Denied |
| Viewer | Denied |

Authorization is DB-backed on each request.

## Search, calendar, and bounds

- List view: server-side pagination (default 20, max 100)
- Calendar view: bounded weekly query (max 92-day range validation)
- Filters: date range, location, status, customer, vehicle

## Deferred capabilities

- Technician assignment on appointments
- Workshop bay / lift capacity scheduling
- Customer portal booking
- External calendar sync
- Recurrence and reminders

Explicit appointment → repair order conversion is implemented in STEP 11. Automatic conversion on appointment create/confirm remains deferred.

See [repair-order-core-v1.md](repair-order-core-v1.md).
