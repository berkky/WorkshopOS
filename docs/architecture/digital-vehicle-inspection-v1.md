# Digital Vehicle Inspection v1

STEP 13 introduces a commercial Digital Vehicle Inspection (DVI) foundation for WorkshopOS repair orders.

## Scope

- Create inspection for an eligible repair order
- Server-controlled default structured checklist (snapshot `InspectionItem` rows)
- Draft → InProgress → Completed lifecycle
- Structured item conditions and optional technician notes
- Manager/service-advisor create authority
- Manager or assigned technician execution
- Completed inspections are immutable historical records
- Repair order, operations board, and My Work integration

**Not in STEP 13:** video, customer approval, estimates, PDF, signatures, share links, customer portal, AI diagnosis, custom template editor.

Photo evidence implemented in STEP 14 — see [inspection-media-v1.md](inspection-media-v1.md).

Estimate foundation (STEP 15) may show latest inspection findings as read-only context when building quotes — see [estimate-and-approval-v1.md](estimate-and-approval-v1.md). DVI does not auto-create priced estimate lines.

## Inspection vs RepairOrder

`Inspection` belongs to a `RepairOrder` workflow but has its **own lifecycle**.

| Concern | Inspection | RepairOrder |
|--------|------------|-------------|
| Create | Manager (Owner/Admin/ServiceAdvisor) | Manager |
| Start work | `StartInspection` (Draft→InProgress) | `StartRepairOrder` |
| Complete | `CompleteInspection` (all items inspected) | `CompleteRepairOrder` |
| Terminal | Completed (immutable) | Completed / Cancelled |

Inspection start/complete **does not** change repair order status.

## Tenant ownership

- `Inspection` and `InspectionItem` are organization-owned entities
- `OrganizationId` is resolved from `IOrganizationContext` only
- Composite FKs enforce same-tenant relationships:
  - `(OrganizationId, RepairOrderId)` → `RepairOrder`
  - `(OrganizationId, InspectionId)` → `Inspection`

## Default checklist (server-controlled)

`DefaultVehicleInspectionTemplate` in Application defines ~20 items across six sections:

- Exterior & Body
- Tires & Wheels
- Brakes
- Engine Bay / Fluids
- Lights & Electrical
- Interior & Safety

On create, the server persists snapshot rows. Clients cannot submit authoritative item names, sections, or sort order.

## Item conditions

`InspectionCondition` enum (persisted structured values):

| Value | Meaning |
|-------|---------|
| `NotChecked` | Not yet evaluated (blocks completion) |
| `Good` | No action currently indicated |
| `Monitor` | Monitor over time |
| `Attention` | Should be reviewed/serviced |
| `Critical` | Significant issue requiring attention |

## Lifecycle rules

1. **Create** — manager only; atomic inspection + template items; repair order must be Draft or InProgress
2. **Start** — Draft → InProgress (idempotent if already InProgress)
3. **Update items** — InProgress only; subset updates allowed; Serializable transaction
4. **Complete** — InProgress only; every item must have `Condition != NotChecked`; empty inspection cannot complete; Serializable transaction
5. **Completed** — terminal; no item updates, no restart

## Authorization

### InspectionManager (ASP.NET policy)

DB-backed membership roles: Owner, Administrator, ServiceAdvisor.

Denied: Technician, Viewer. Role changes in DB take effect immediately (no stale claims).

### Technician execution

Assigned technician may start/update/complete when:

- Active organization membership
- Linked `StaffMember` in current organization
- `StaffMember.Status == Active`
- `StaffMember.Position == Technician`
- Active `RepairOrderTechnicianAssignment` on the inspection's repair order

Owner with linked technician profile and active assignment may execute without Technician membership role.

Unlinked staff (`UserId == null`) may be assigned operationally but cannot self-service.

## MVC routes

| Method | Route | Policy |
|--------|-------|--------|
| GET | `/inspections` | OrganizationMember |
| POST | `/repair-orders/{repairOrderId}/inspections` | InspectionManager |
| GET | `/inspections/{inspectionId}` | OrganizationMember |
| POST | `/inspections/{inspectionId}/start` | OrganizationMember + service auth |
| POST | `/inspections/{inspectionId}/items` | OrganizationMember + service auth |
| POST | `/inspections/{inspectionId}/complete` | OrganizationMember + service auth |

No DELETE routes. All POST actions use antiforgery tokens.

## Concurrency

Item updates and completion use PostgreSQL `Serializable` transactions. Serialization conflicts return a safe business conflict result.

## Future

- Video / document evidence
- Customer approval workflow
- Estimate generation from findings
