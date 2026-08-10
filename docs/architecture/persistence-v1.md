# WorkshopOS Persistence v1

## Overview

WorkshopOS v1 persistence targets **PostgreSQL** with a **shared database / shared schema** model and `OrganizationId` as the tenant discriminator.

This document describes the **generated EF Core model and InitialCreate migration**. The schema is **designed to enforce** tenant-safe relationships but has **not** been applied to a PostgreSQL database yet.

## Technology

| Component | Version / choice |
|---|---|
| Database | PostgreSQL |
| ORM | EF Core 10.0.10 |
| Provider | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 |
| Project | `WorkshopOS.Infrastructure` |

## Identity generation

- Primary keys are `uuid` (`Guid`).
- IDs are **application-generated** using `Guid.CreateVersion7()` in the domain `Entity` base class.
- EF mapping uses `ValueGeneratedNever` for all primary keys.
- No PostgreSQL `gen_random_uuid()` or `uuid_generate_v4()` defaults.

Primary key remains `Id`. Organization-owned principals also expose alternate keys:

```text
(OrganizationId, Id)
```

## Tenant query filters

Named global query filter: `OrganizationFilter`.

Applied to all organization-owned operational entities:

- `WorkshopLocation`
- `Customer`
- `Vehicle`
- `Appointment`
- `RepairOrder`
- `Inspection`
- `InspectionItem`
- `Estimate`
- `EstimateItem`
- `EstimateShare`
- `ServiceCatalogItem`
- `PartCatalogItem`
- `PartInventoryBalance`
- `PartInventoryMovement`

Behavior (fail-closed):

```text
Current organization unresolved → tenant-owned queries return no rows
Current organization resolved   → rows must match OrganizationId
```

`Organization` does **not** use this filter. Tenant-facing access to organization root records will be constrained by application authorization in a future step.

**Global query filters are defense-in-depth, not complete authorization.**

## Cross-tenant foreign keys

Cross-tenant FK protection is enforced in the EF relational model using composite foreign keys:

```text
(OrganizationId, ReferencedEntityId)
    → Principal (OrganizationId, Id)
```

Examples:

- `Vehicle (OrganizationId, CurrentCustomerId)` → `Customer (OrganizationId, Id)`
- `Appointment` → `WorkshopLocation`, `Customer`, `Vehicle` (all composite)
- `RepairOrder` → location, customer, vehicle, optional appointment (all composite)
- `Inspection` / `Estimate` / child items → parent repair order or estimate (composite)

## Delete behavior

Operational relationships use `DeleteBehavior.Restrict`. Accidental cascade deletion of business history is not permitted in v1.

## Time contract

- All `*Utc` properties map to PostgreSQL `timestamp with time zone`.
- Persisted values must have `Offset == TimeSpan.Zero`.
- Business timezone is stored separately as IANA `TimeZoneId` on `Organization` and optionally `WorkshopLocation`.

## Money contract

- `EstimateItem.UnitPrice`: `numeric(18,2)`
- `EstimateItem.Quantity`: `numeric(12,3)`
- No persisted line total column; totals are derived as `Quantity * UnitPrice`.

## Enum storage

Domain enums are stored as PostgreSQL `integer`. No PostgreSQL native enum types.

## SaveChanges guards (persistence layer)

`AppDbContext` applies:

1. **Audit timestamps** for `IHasTimestamps` entities (`CreatedAtUtc`, `UpdatedAtUtc`)
2. **Tenant write guard** for `IOrganizationOwnedEntity` (requires resolved organization context)
3. **UTC offset validation** for properties ending in `Utc`

## Design-time vs runtime

| Concern | Status |
|---|---|
| `AppDbContext` | Implemented |
| Entity configurations | Implemented |
| `InitialCreate` migration | Generated |
| Migration applied to database | **Applied** to isolated local `workshopos_dev` and `workshopos_test` |
| Identity schema | **Applied** via `AddIdentityAndOrganizationMemberships` |
| Runtime `AddDbContext` in Web | **Implemented** |
| Integration test project | **Implemented** — verified against real PostgreSQL |
| Connection strings in source | **Not present** |
| Automatic migration on startup | **Not implemented** |
| Automatic seed | **Not implemented** |

`InitialCreate` is applied to isolated local WorkshopOS development and test PostgreSQL databases. No production database has been provisioned or migrated. Application startup does not automatically migrate or seed.

Design-time factory reads `WORKSHOPOS_DESIGNTIME_CONNECTION` from the environment. No credentials are stored in source control.

## Migration history

| Migration | Purpose |
|---|---|
| `20260809131115_InitialCreate` | Operational tenant schema |
| `20260809190429_AddIdentityAndOrganizationMemberships` | ASP.NET Identity + `OrganizationMemberships` |
| `20260810093254_AddWorkshopTeamManagement` | `StaffMembers` + `StaffLocationAssignments` |
| `20260810105310_AddWorkshopOperationsFoundation` | `RepairOrder.Priority`, `RepairOrderTechnicianAssignments` |
| `20260810113926_AddInspectionMediaFoundation` | `InspectionMediaAssets`, inspection item alternate key |
| `20260810120537_EnhanceEstimateApprovalFoundation` | `Estimate.SentAtUtc`, `ApprovedAtUtc`, `DeclinedAtUtc` |
| `20260810123853_AddSecureEstimateCustomerSharing` | `EstimateShares` — secure customer portal sharing |
| `20260810131936_AddCatalogAndInventoryFoundation` | Service/parts catalog + inventory balances/movements |
| `20260810140103_AddInvoiceAndPaymentFoundation` | `Invoices`, `InvoiceItems`, `InvoicePaymentRecords`, `RepairOrders.CommerciallyClosedAtUtc` |

No seed data. No production database has been provisioned or migrated.

## Migration

- Name: `InitialCreate`
- Path: `src/WorkshopOS.Infrastructure/Persistence/Migrations/`
- Contains: `CreateTable`, indexes, unique constraints, alternate keys, foreign keys
- Does **not** contain: seed data, raw SQL, database UUID defaults, cascade deletes

## Future concerns (not in v1)

- Odometer unit (km vs mile)
- Customer/vehicle unique normalization rules
- Optimistic concurrency tokens
- Soft delete
- Platform subscription tables
