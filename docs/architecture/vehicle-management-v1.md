# WorkshopOS Vehicle Management v1

## Vehicle is operational tenant data

`Vehicle` is a tenant-owned operational record for workshop vehicle identity and current customer association.

It is separate from `ApplicationUser` and is not authentication identity.

## Current customer vs historical repair customer

```text
Vehicle.CurrentCustomerId
= current ownership / customer association

RepairOrder.CustomerId
= customer captured for that historical repair job
```

Reassigning `Vehicle.CurrentCustomerId` must never rewrite existing `RepairOrder.CustomerId` values.

## Tenant ownership

`Vehicle.OrganizationId` is server-controlled from resolved `IOrganizationContext`.

Client commands and forms must not supply:

```text
Id
OrganizationId
CreatedAtUtc
UpdatedAtUtc
```

## Same-tenant customer assignment

`Vehicle (OrganizationId, CurrentCustomerId) → Customer` uses the existing composite foreign key.

Cross-tenant customer assignment is rejected by service validation and PostgreSQL constraints.

## VehicleManager authorization

Vehicle write operations require the `VehicleManager` policy backed by `OrganizationMembership.Role`:

| Role | Vehicle writes |
|---|---|
| Owner | Allowed |
| Administrator | Allowed |
| ServiceAdvisor | Allowed |
| Technician | Denied |
| Viewer | Denied |

Authorization is DB-backed on each request.

## Vehicle identity semantics

- `Vehicle.Id` is internal database identity.
- VIN/chassis and license plate are business attributes, not authorization keys.
- VIN and license plate are trimmed and uppercased invariant on input.
- Make/model are trimmed while preserving meaningful display casing.
- No global uniqueness constraints on VIN or license plate.

## Lifecycle

The current schema has no `IsActive` / status field on `Vehicle`.

STEP 09 does not add lifecycle schema. No hard delete is implemented.

## Search and pagination

Vehicle list/search is server-side, tenant-filtered, and paginated (default 20, max 100).

Search supports make, model, license plate, VIN/chassis, and current customer display name.

## Customer integration

Customer details show the real current vehicle count and link to the canonical `/vehicles` list filtered by customer.

Vehicle CRUD remains under `VehiclesController` only.

## Service history summary

Vehicle details show read-only repair order count, latest status, and links to the repair order list.

See [repair-order-core-v1.md](repair-order-core-v1.md) for walk-in intake, appointment conversion, and lifecycle.

## Deferred capabilities

- Repair order CRUD/workflow
- Vehicle ownership history ledger
- VIN decoding / external APIs
- Customer portal vehicle access
- Hard delete
