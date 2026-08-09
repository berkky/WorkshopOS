# WorkshopOS Domain Model v1

## Tenant root

`Organization` is the commercial tenant root. Every workshop business using WorkshopOS belongs to exactly one organization.

`WorkshopLocation` represents a physical service site within an organization. A single-location business uses one location; multi-branch businesses use multiple locations under the same organization.

## Entity hierarchy

```text
Organization
    └── WorkshopLocation

Organization
    ├── Customer
    ├── Vehicle
    ├── Appointment
    ├── RepairOrder
    │     ├── Inspection
    │     │      └── InspectionItem
    │     └── Estimate
    │            └── EstimateItem
```

## Core aggregates

| Entity | Role |
|---|---|
| `Organization` | Tenant root; owns currency and default timezone |
| `WorkshopLocation` | Physical service site |
| `Customer` | Organization customer record (not an authentication identity) |
| `Vehicle` | Organization vehicle record |
| `Appointment` | Scheduled visit / booking |
| `RepairOrder` | Operational work order (core commercial aggregate) |
| `Inspection` | Digital vehicle inspection linked to a repair order |
| `InspectionItem` | Line item within an inspection |
| `Estimate` | Customer-facing quote linked to a repair order |
| `EstimateItem` | Priced line within an estimate |

## Appointment vs RepairOrder

An **Appointment** is a scheduled visit. It represents intent and calendar planning:

- customer booked a time slot
- vehicle is expected to arrive
- concern may be captured before work begins

A **RepairOrder** is the actual service job. It represents operational work:

- diagnosis and repair execution
- inspections and estimates
- lifecycle from draft through completion

An appointment may optionally spawn or link to a repair order (`RepairOrder.AppointmentId`), but they remain separate concepts with separate lifecycles.

## Vehicle current customer vs RepairOrder historical customer

`Vehicle.CurrentCustomerId` represents the **current** ownership or association at the vehicle level.

`RepairOrder.CustomerId` represents the **customer at the time the repair order was opened**. This value is historical truth and must not change when vehicle ownership changes later.

Example:

1. Vehicle owned by Customer A
2. Repair order opened for Customer A
3. Vehicle later associated with Customer B (`CurrentCustomerId` updated)
4. Original repair order still records Customer A

Customer-vehicle history tables are not part of v1 and may be introduced later if required.

## Identity strategy

- Primary keys use `Guid` generated via `Guid.CreateVersion7()` in the shared `Entity` base class.
- Domain primary `Id` values are **not** intended for public customer URLs or share links.
- Future public-facing identifiers (`PublicId`, `SecureToken`, `ShareToken`) will be designed separately.

## Time strategy

- Absolute timestamps use `DateTimeOffset` with explicit `Utc` suffix in property names.
- Organization and location store IANA timezone identifiers (e.g. `Europe/Istanbul`).
- Display-time conversion is an application concern, not embedded in domain entities.

## Money strategy

- Financial amounts use `decimal` (`EstimateItem.Quantity`, `EstimateItem.UnitPrice`).
- Currency is explicit per estimate (`CurrencyCode`) with organization default (`DefaultCurrencyCode`).
- No FX conversion or payment integration in v1 domain.

## Authentication boundary (planned)

Authentication identity (`ApplicationUser`, staff membership, technician roles) is intentionally **not** modeled in v1 domain. Customer records are operational data, not login accounts.

Planned future separation:

```text
Identity Account
        ↕
Organization Membership
        ↕
Staff Member / Technician
```

## Explicitly out of scope for v1 domain

- Inventory / parts
- Invoices / payments
- Subscriptions / billing (platform boundary)
- AI features
- Media attachments on inspections
- Soft-delete flags on all entities
