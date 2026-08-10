# WorkshopOS Staff and Team v1

## Three separate concepts

| Concept | Purpose |
|---|---|
| `ApplicationUser` | Global login identity |
| `OrganizationMembership` | Tenant authorization + organization role |
| `StaffMember` | Operational employee/team profile |

Do not merge these models.

`StaffPosition` describes operational work classification. It is **not** authorization.

`OrganizationMembership.Role` grants authorization. Never authorize because `StaffPosition == Manager`.

## StaffMember

Tenant-owned operational profile with optional login link:

```text
StaffMember.UserId? → OrganizationMembership (same Organization)
```

A staff profile may exist without login access (invitation/provisioning is future work).

Staff operational lifecycle:

```text
Active
Inactive
```

Inactive staff remains historical operational data. No hard-delete workflow in STEP 07.

## Staff inactivity vs membership suspension

These lifecycles are intentionally separate:

- `StaffMember.Status = Inactive` does **not** suspend/revoke `OrganizationMembership`
- Membership suspension/revocation does **not** deactivate/delete `StaffMember`

## StaffLocationAssignment

Tenant-owned join between `StaffMember` and `WorkshopLocation`.

Composite same-tenant foreign keys enforce:

```text
(OrganizationId, StaffMemberId) → StaffMember
(OrganizationId, WorkshopLocationId) → WorkshopLocation
```

Unique `(OrganizationId, StaffMemberId, WorkshopLocationId)` prevents duplicate assignments.

## Database safety

- `(OrganizationId, UserId)` unique on `StaffMember` when `UserId` is not null
- Composite FK to `OrganizationMembership` prevents cross-tenant user links
- `Restrict` delete behavior on team relationships

## Membership role operations

Role changes use `OrganizationMembership.Role` only — not ASP.NET Identity roles.

| Actor | May manage |
|---|---|
| Owner | All memberships (subject to last-owner protection) |
| Administrator | ServiceAdvisor, Technician, Viewer only |

Administrators may not grant Owner/Administrator or modify Owner/Administrator memberships.

Self role/status mutation is rejected in STEP 07.

## Last active Owner invariant

An organization must never end with zero active Owners.

Demoting, suspending, or revoking the last active Owner is rejected inside a `Serializable` database transaction.

## Team routes (STEP 07)

| Route | Authorization |
|---|---|
| `GET /team` | OrganizationMember |
| Team write operations | OrganizationManager + service-level membership revalidation |

POST endpoints use antiforgery tokens.

## Out of scope (STEP 07)

- Employee invitations / account provisioning
- Password creation for staff
- Payroll, shifts, scheduling
- Repair-order technician assignment (STEP 12 — see [workshop-operations-v1.md](workshop-operations-v1.md))
- Hard-delete staff workflow

## Production readiness

STEP 07 is a foundation slice for workshop team management. It is not production-ready HR or invitation workflow without dedicated later steps.
