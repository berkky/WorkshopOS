# WorkshopOS Customer CRM v1

## Customer is operational CRM data

`Customer` is a tenant-owned operational record for workshop CRM contact management.

It is **not** authentication identity and is **not** linked to `ApplicationUser` in STEP 08.

## Separation from ApplicationUser

```text
ApplicationUser     = global login identity
OrganizationMembership = tenant authorization
Customer            = operational CRM record
```

Do not add customer portal login, passwords, or Identity roles to `Customer`.

## Tenant ownership

`Customer.OrganizationId` is server-controlled from resolved `IOrganizationContext`.

Client commands and forms must not supply:

```text
Id
OrganizationId
CreatedAtUtc
UpdatedAtUtc
```

## Tenant isolation

Customer reads and writes use the existing `OrganizationFilter` and tenant write guard.

Cross-tenant customer identifiers return safe not-found behavior without revealing other-tenant existence.

## CustomerManager authorization

Customer write operations require the `CustomerManager` policy backed by `OrganizationMembership.Role`:

| Role | Customer CRM writes |
|---|---|
| Owner | Allowed |
| Administrator | Allowed |
| ServiceAdvisor | Allowed |
| Technician | Denied |
| Viewer | Denied |

Authorization is DB-backed on each request — not from stale cookie role claims.

## Contact data semantics

Customer email and phone are CRM contact fields.

They are **not**:

- Identity login identifiers
- globally unique keys
- email verification workflow

Multiple customers may share the same email or phone within normal business practice.

## Lifecycle

`Customer.IsActive` supports active/inactive lifecycle.

No hard-delete workflow in STEP 08. Deactivated customers remain historical records.

Staff inactivity and membership suspension are separate lifecycles.

## Search and pagination

Customer list uses server-side search (name, email, phone) with tenant filter active.

Pagination defaults to page size 20, maximum 100, with deterministic ordering by display name then id.

## Out of scope (STEP 08)

- Customer portal authentication
- Vehicle CRUD (STEP 09)
- CRM activity timeline entities
- Address framework
- Marketing automation, tags, lead scoring
- Hard delete

## Production readiness

STEP 08 provides foundational tenant-safe customer CRM. It is not a complete commercial CRM suite without vehicle management, communications history, and customer portal workflows in later steps.
