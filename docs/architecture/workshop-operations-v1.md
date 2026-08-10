# Workshop Operations (v1)

STEP 12 introduces the first workshop operations capability: repair order queue, technician assignment, priority, technician work state, operations board, and My Work.

## RepairOrder operations board

`GET /operations` shows active (non-terminal) repair orders grouped by status:

- **Draft / intake** — `Draft` jobs
- **In progress** — `InProgress` jobs
- **Other active** — remaining non-terminal statuses

Completed and cancelled repair orders are excluded from the default active board (filterable via status).

Summary counts (database-derived): Active jobs, Unassigned, In progress, Urgent.

Board query is bounded to **200** results; truncated results prompt narrower filters.

Queue ordering: **Priority descending** (Urgent → High → Normal → Low), then **OpenedAtUtc ascending**, then **Id**.

## RepairOrderPriority

| Value | Meaning |
|-------|---------|
| Low | 1 |
| Normal | 2 (default on create) |
| High | 3 |
| Urgent | 4 |

Priority is operational queue metadata. It does not change authorization or historical customer/vehicle identity.

Managers (Owner, Administrator, ServiceAdvisor) may change priority on non-terminal repair orders. Technicians cannot.

## RepairOrderTechnicianAssignment

Tenant-owned entity linking a repair order to an operational `StaffMember` technician.

| Field | Role |
|-------|------|
| `RepairOrderId`, `StaffMemberId` | Assignment targets |
| `WorkStatus` | Technician work state |
| `AssignedAtUtc` | When assigned |
| `StartedAtUtc` | When technician started work |
| `WorkCompletedAtUtc` | When technician marked work complete |
| `UnassignedAtUtc` | When assignment ended (null = active) |

Assignments are **never hard-deleted**. Reassignment sets `UnassignedAtUtc` on the previous row and creates a new active assignment.

## One active technician per repair order

PostgreSQL partial unique index enforces at most one active assignment per repair order within an organization:

```sql
UNIQUE (OrganizationId, RepairOrderId) WHERE UnassignedAtUtc IS NULL
```

Historical rows with `UnassignedAtUtc` set remain; multiple historical assignments per repair order are allowed.

## Same-tenant composite foreign keys

- `(OrganizationId, RepairOrderId)` → `RepairOrders`
- `(OrganizationId, StaffMemberId)` → `StaffMembers`

Cross-tenant assignment fails at PostgreSQL level (SQLSTATE `23503`).

## Technician eligibility

A `StaffMember` may be assigned only if:

- Belongs to current organization
- `Status == Active`
- `Position == Technician`
- Has `StaffLocationAssignment` for the repair order's `WorkshopLocationId`

`StaffPosition` describes operational eligibility, **not** application authorization.

## Staff without login

A technician `StaffMember` with `UserId = null` may be assigned by managers but cannot use My Work or technician self-service until linked to an authenticated member.

## TechnicianWorkStatus vs RepairOrderStatus

| Layer | Enum | Meaning |
|-------|------|---------|
| Repair order | `RepairOrderStatus` | Whole workshop job lifecycle |
| Assignment | `TechnicianWorkStatus` | Assigned technician's work on that job |

Technician work transitions: `Assigned` → `InProgress` → `WorkCompleted` (terminal for that assignment). No backward transitions.

## Technician start work

Authorized assigned technician starting work:

1. Assignment: `Assigned` → `InProgress`, sets `StartedAtUtc`
2. If repair order is `Draft`: also calls `RepairOrder.StartWork()` → `InProgress`
3. If repair order is already `InProgress`: does not fail
4. Terminal repair orders: rejected

## WorkCompleted ≠ RepairOrder completed

`TechnicianWorkStatus.WorkCompleted` does **not** call `RepairOrder.Complete()`. The repair order may remain `InProgress` until a manager/service advisor completes it via STEP 11 workflow.

## Manager operations

Membership roles: Owner, Administrator, ServiceAdvisor (DB-backed, not stale claims).

| Action | Route |
|--------|-------|
| Assign | `POST /operations/repair-orders/{id}/assign` |
| Reassign | `POST /operations/repair-orders/{id}/reassign` |
| Unassign | `POST /operations/repair-orders/{id}/unassign` |
| Change priority | `POST /operations/repair-orders/{id}/priority` |

Controller policy: `RepairOrderManager`. Service revalidates membership from database.

## Technician self-work authorization

Requires **both**:

- Active `OrganizationMembership` for current organization
- Linked active `StaffMember` with `Position == Technician`
- Active assignment on the repair order for that staff member

An Owner membership role with a linked Technician `StaffMember` may perform own work — membership role does not need to be Technician.

## My Work

`GET /work/my-jobs` lists active assignments for the authenticated user's linked technician profile.

| Work status | Action |
|-------------|--------|
| Assigned | Start work |
| InProgress | Mark work complete |
| WorkCompleted | No action |

Routes: `POST /work/repair-orders/{id}/start`, `POST /work/repair-orders/{id}/complete`

## Not in STEP 12

- Multi-technician jobs
- Drag-and-drop board
- Job timers / payroll / timesheets
- DVI (STEP 13–14), estimates (STEP 15), parts, invoices
- Automatic repair order completion on technician work complete

## Deferred

- Inspection workflow (STEP 13) — implemented; see [digital-vehicle-inspection-v1.md](digital-vehicle-inspection-v1.md)
- Estimate workflow (STEP 15) — implemented; operations board shows latest estimate status indicator
- Catalog/inventory (STEP 17) — technician work status changes do not adjust stock; see [catalog-and-inventory-v1.md](catalog-and-inventory-v1.md)
- Premium drag/drop UX on same server rules
- Management reporting (STEP 19) — read-only operational KPIs; see [reporting-and-analytics-v1.md](reporting-and-analytics-v1.md)
