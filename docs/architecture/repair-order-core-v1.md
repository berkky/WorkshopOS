# Repair Order Core Workflow (v1)

STEP 11 establishes the commercial repair order foundation: walk-in intake, explicit appointment conversion, service-intake editing, and a controlled workshop-job lifecycle.

## Appointment vs RepairOrder

| Concept | Role |
|--------|------|
| **Appointment** | Scheduled / pre-service intent. Historical scheduling data. |
| **RepairOrder** | Actual workshop job once work is opened in the shop. |

Rules:

- Creating a repair order from an appointment is an **explicit** operation (`POST /repair-orders/from-appointment/{appointmentId}`).
- Appointments are **never** auto-converted when created or confirmed.
- Appointment and repair order status enums remain separate.
- Appointments are **not** deleted after repair order creation.
- Repair order lifecycle does **not** mutate appointment status in STEP 11.

## Walk-in vs appointment conversion

**Walk-in** (`POST /repair-orders/create`): requires existing customer, vehicle, and active workshop location in the current organization. `AppointmentId` remains null.

**Appointment conversion**: server uses the appointment as the authoritative source for `CustomerId`, `VehicleId`, `WorkshopLocationId`, and `AppointmentId`. Optional intake fields on the command override appointment concern/notes when provided.

## Historical customer identity

At intake, `RepairOrder.CustomerId`, `VehicleId`, and `WorkshopLocationId` capture job identity.

`Vehicle.CurrentCustomerId` reflects the **current** vehicle/customer relationship. Changing it later (vehicle reassignment) must **never** rewrite existing `RepairOrder.CustomerId`.

Normal repair order edit updates service-intake fields only; customer, vehicle, and appointment link are not exposed for casual mutation.

## Server-generated repair order number

- Format: `RO-{yyyyMMdd}-{8-char suffix}` (suffix from UUIDv7 random portion).
- Generated server-side only; not accepted from public create forms.
- Unique per organization via `(OrganizationId, Number)` database constraint.
- Number is a display identifier, not an authorization mechanism.

## Tenant ownership

`RepairOrder` is organization-owned. `OrganizationId` comes only from `IOrganizationContext`. Unresolved context fails closed.

## Same-tenant composite foreign keys

PostgreSQL composite FKs enforce:

- `(OrganizationId, WorkshopLocationId)` → `WorkshopLocations`
- `(OrganizationId, CustomerId)` → `Customers`
- `(OrganizationId, VehicleId)` → `Vehicles`
- `(OrganizationId, AppointmentId)` → `Appointments` (when non-null)

Cross-tenant relationship creation fails at the database level (SQLSTATE `23503`).

## Duplicate appointment conversion

`CreateRepairOrderFromAppointmentAsync` runs inside a **Serializable** transaction when no outer transaction exists. Within the organization, an existing repair order with the same `AppointmentId` is rejected before insert. Concurrent duplicate attempts map to safe failure (`DuplicateAppointmentRepairOrder` or `ConcurrencyConflict`).

## Lifecycle (STEP 11 minimal)

Uses existing `RepairOrderStatus` enum:

| Status | STEP 11 role |
|--------|----------------|
| `Draft` | Initial state on create |
| `InProgress` | Active work (`StartWork` from `Draft`) |
| `Completed` | Terminal (`Complete` from `InProgress`; sets `CompletedAtUtc` via `TimeProvider`) |
| `Cancelled` | Terminal (`Cancel` from `Draft` or `InProgress`) |

Other enum values (`Diagnosis`, `AwaitingApproval`, etc.) exist for later STEPs but are not used in STEP 11 transitions.

Terminal states (`Completed`, `Cancelled`) cannot return to active work. Lifecycle changes use explicit POST actions, not arbitrary form status assignment.

No hard delete.

## RepairOrderManager authorization

Membership-role based (database-backed, not stale claims):

| Role | Write |
|------|-------|
| Owner | Allowed |
| Administrator | Allowed |
| ServiceAdvisor | Allowed |
| Technician | Denied |
| Viewer | Denied |

Read: `OrganizationMember`.

## Not in STEP 11

- Estimate approval
- Invoice / payment
- Parts inventory
- Technician assignment / job board

Implemented in STEP 12 — see [workshop-operations-v1.md](workshop-operations-v1.md).

Inspection workflow implemented in STEP 13 — see [digital-vehicle-inspection-v1.md](digital-vehicle-inspection-v1.md).

Estimate workflow implemented in STEP 15 — see [estimate-and-approval-v1.md](estimate-and-approval-v1.md). Multiple estimates per repair order are allowed; estimate decisions do not change repair order status.

Parts catalog and location inventory (STEP 17) — see [catalog-and-inventory-v1.md](catalog-and-inventory-v1.md). Repair order lifecycle does not automatically consume stock.

Invoice and payment workflow (STEP 18) — see [invoice-and-payment-v1.md](invoice-and-payment-v1.md). `CommerciallyClosedAtUtc` is separate from `RepairOrderStatus.Completed`.

Still deferred:

- Customer portal
- Inspection media / photos
- Full automatic estimate generation from inspection findings

These are later STEPs.

## MVC routes

| Route | Purpose |
|-------|---------|
| `GET /repair-orders` | List / filter / search |
| `GET/POST /repair-orders/create` | Walk-in intake |
| `POST /repair-orders/from-appointment/{appointmentId}` | Explicit conversion |
| `GET /repair-orders/{repairOrderId}` | Details |
| `GET/POST /repair-orders/{repairOrderId}/edit` | Service intake edit |
| `POST /repair-orders/{repairOrderId}/start` | Start work |
| `POST /repair-orders/{repairOrderId}/complete` | Complete |
| `POST /repair-orders/{repairOrderId}/cancel` | Cancel |

All POST routes use antiforgery tokens. No GET mutations. No delete route.
