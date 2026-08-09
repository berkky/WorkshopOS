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

## Cross-tenant relationship prevention (planned)

Persistence will evaluate composite integrity so that, for example:

```text
RepairOrder.OrganizationId = Organization A
RepairOrder.VehicleId      = vehicle belonging to Organization B
```

cannot be persisted.

Target constraint pattern:

```text
OrganizationId + EntityId
```

Composite foreign keys and indexes will be designed in the persistence step. They are **not** implemented in v1 domain code.

## Global query filters (planned)

EF Core global query filters scoped by `OrganizationId` will be applied in a future persistence layer. They supplement — but do not replace — authorization and service-level checks.

## Platform data boundary

Platform/SaaS data (subscriptions, plans, feature entitlements) lives outside the organization operational boundary. Subscription is **not** a parent of customers, vehicles, or repair orders.
