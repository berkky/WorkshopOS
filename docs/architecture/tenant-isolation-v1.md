# WorkshopOS Tenant Isolation v1

## Tenant model

| Concept | Value |
|---|---|
| Tenant root | `Organization` |
| Tenant discriminator | `OrganizationId` on all organization-owned operational records |
| Initial persistence strategy | Single PostgreSQL database, shared schema, `OrganizationId` column |

The technical term **tenant** maps to the business concept **organization**. Domain code uses `Organization` and `OrganizationId`, not `Tenant` / `TenantId`.

## Organization-owned entities

The following entities carry an explicit `OrganizationId`:

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
- `Invoice`
- `InvoiceItem`
- `InvoicePaymentRecord`
- `StaffMember`
- `StaffLocationAssignment`

`Organization` itself does **not** carry `OrganizationId` because it is the tenant root.

## Defense in depth

Tenant-owned rows must **never** be queried or mutated without an organization scope.

Application-level filtering alone is **not** considered complete security.

Planned isolation layers:

```text
Authenticated organization context
    → application/service authorization
    → EF Core tenant filtering
    → organization-scoped relationships and composite indexes
    → PostgreSQL-level constraints where appropriate
```

## Explicit OrganizationId on child rows

Child entities such as `InspectionItem` and `EstimateItem` carry their own `OrganizationId` even though they also reference a parent (`InspectionId`, `EstimateId`).

Rationale:

- defense in depth for query filtering
- simpler tenant-scoped indexes
- reduced risk of cross-tenant joins when parent context is missing

Implicit tenant inheritance through parent foreign keys alone is **not** sufficient.

## Cross-tenant relationship prevention

The `InitialCreate` migration generated in STEP 03 models composite foreign keys so that, for example:

```text
RepairOrder.OrganizationId = Organization A
RepairOrder.VehicleId      = vehicle belonging to Organization B
```

cannot be represented in the generated schema.

Constraint pattern:

```text
OrganizationId + EntityId
```

Composite foreign keys and alternate keys `(OrganizationId, Id)` are **implemented in the EF model**, **generated in the InitialCreate migration**, and **verified against isolated local PostgreSQL**.

`OrganizationFilter` tenant isolation verified against isolated local PostgreSQL.

Composite cross-tenant foreign-key rejection verified against isolated local PostgreSQL.

Tenant write guard fail-closed behavior verified through the integration test suite.

## Authenticated tenant resolution (STEP 05)

```text
Authentication
→ OrganizationMembership lookup
→ Organization resolution
→ Authorization
→ tenant-filtered operational queries
```

Authentication is not the only isolation layer. Existing defenses remain required:

```text
OrganizationFilter
composite FK
write guard
```

`OrganizationMembership` is intentionally not tenant-filtered because it is required to establish tenant context. Membership access must remain authorization-controlled.

## Owner onboarding tenant bootstrap (STEP 06)

During owner onboarding, tenant context for the first `WorkshopLocation` write is resolved only after trusted membership validation confirms an active owner membership for the newly created organization. Controllers do not expose raw `SetOrganizationId(Guid)` helpers.

## Multi-organization selection hint (STEP 06)

Authenticated users with multiple active memberships may select an organization at `/organization/select`. The resulting `WorkshopOS.SelectedOrganization` claim is revalidated from the database during resolution; stale or invalid hints fail closed.

## Customer CRM (STEP 08)

`Customer` remains tenant-filtered operational CRM data. `CustomerManager` authorization uses membership roles (Owner, Administrator, ServiceAdvisor). Customer email/phone are contact data, not authentication identity.

See [customer-crm-v1.md](customer-crm-v1.md).

## Vehicle management (STEP 09)

`Vehicle` remains tenant-filtered operational data. `VehicleManager` authorization uses membership roles (Owner, Administrator, ServiceAdvisor). `Vehicle.CurrentCustomerId` represents current customer association; `RepairOrder.CustomerId` remains historical and is not rewritten by ownership reassignment.

See [vehicle-management-v1.md](vehicle-management-v1.md).

## Appointment scheduling (STEP 10)

`Appointment` remains separate from `RepairOrder`. `AppointmentManager` authorization uses membership roles (Owner, Administrator, ServiceAdvisor). Vehicle overlap protection uses active appointment statuses and serializable transactions for create/reschedule.

See [appointment-scheduling-v1.md](appointment-scheduling-v1.md).

## Repair order core workflow (STEP 11)

`RepairOrder` is tenant-filtered operational data with composite FK protection to customer, vehicle, location, and optional appointment. `RepairOrderManager` authorization uses membership roles (Owner, Administrator, ServiceAdvisor). Historical `RepairOrder.CustomerId` is preserved when `Vehicle.CurrentCustomerId` changes. Duplicate appointment conversion is prevented via serializable service boundary.

See [repair-order-core-v1.md](repair-order-core-v1.md).

## Workshop operations (STEP 12)

`RepairOrderTechnicianAssignment` is tenant-filtered with composite FKs to repair orders and staff members. One active technician per repair order is enforced by a partial unique index. Technician eligibility uses `StaffPosition` and location assignments; authorization uses membership roles separately.

See [workshop-operations-v1.md](workshop-operations-v1.md).

## Reporting (STEP 19)

Management reporting is read-only and derived from the same tenant-filtered operational and billing tables. `ReportingViewer` authorization uses membership roles (Owner, Administrator, ServiceAdvisor). Customer portal has no reporting access.

See [reporting-and-analytics-v1.md](reporting-and-analytics-v1.md).

## Global query filters

Filters supplement — but do not replace — authorization and service-level checks.

Access to the `Organization` root entity is **not** filtered at the EF layer in v1; tenant-facing organization access will be constrained by application authorization in a future step.

## Platform data boundary

Platform/SaaS data (subscriptions, plans, feature entitlements) lives outside the organization operational boundary. Subscription is **not** a parent of customers, vehicles, or repair orders.
