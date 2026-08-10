# WorkshopOS Identity and Membership v1

## Global identity

`ApplicationUser` is a **global** login identity. It does not belong directly to a single `Organization`.

A user may hold multiple organization memberships with different roles:

```text
ApplicationUser
        ↓
OrganizationMembership
        ↓
Organization
```

Do **not** add `ApplicationUser.OrganizationId`.

## Organization membership

`OrganizationMembership` links a global user to an organization with an organization-specific role and lifecycle status.

| Field | Purpose |
|---|---|
| `OrganizationId` | Target organization |
| `UserId` | Global application user |
| `Role` | Organization-specific role |
| `Status` | Membership lifecycle (`Active`, `Suspended`, `Revoked`) |

Unique constraint: `(OrganizationId, UserId)`.

## Organization roles vs ASP.NET Identity roles

Organization-specific roles live on `OrganizationMembership.Role`:

```text
Owner
Administrator
ServiceAdvisor
Technician
Viewer
```

ASP.NET Identity roles, if used later, are reserved for **global/platform** concerns only (for example a future `PlatformAdministrator`).

Identity roles are **not** used as tenant roles.

## Membership is not tenant-filtered

`OrganizationMembership` is intentionally **not** subject to `OrganizationFilter`.

Reason: membership records are required to resolve the current organization from an authenticated user. Applying the tenant query filter would create a bootstrap cycle:

```text
tenant unresolved → membership filtered → tenant cannot resolve
```

Membership data is access-control infrastructure. It must remain explicitly authorization-controlled and must not be exposed through generic unrestricted repositories.

## Customer and staff separation

- `Customer` is an operational CRM record.
- `ApplicationUser` is a login identity.
- `Customer` is not merged with `ApplicationUser`.
- Staff/technician employment profiles are future work (STEP 07).

## Tenant resolution flow

```text
Authentication
→ OrganizationMembership lookup (by authenticated UserId)
→ Organization resolution (server-side)
→ Authorization
→ tenant-filtered operational queries
```

### Resolution rules (v1)

| Condition | Result |
|---|---|
| 0 active memberships | Organization context remains **unresolved** |
| Exactly 1 active membership + active organization | Organization context **resolved** |
| 2+ active memberships without valid selection | Organization context remains **unresolved** |
| 2+ active memberships + valid selected organization hint | Organization context **resolved** to selected organization |
| Selected organization without membership | **Unresolved** (fail closed on stale hint) |
| Selected suspended/revoked membership | **Unresolved** |
| Selected inactive organization | **Unresolved** |

OrganizationId must **never** be taken from client-controlled route/query/header inputs.

`WorkshopOS.SelectedOrganization` may exist as an authentication claim hint after organization selection, but it is **not** authoritative. Resolution revalidates membership and organization status from the database on every request.

## Owner onboarding (STEP 06)

Public owner onboarding creates a global `ApplicationUser`, `Organization`, owner `OrganizationMembership`, and first `WorkshopLocation` in one atomic PostgreSQL transaction. Sign-in occurs only after commit.

See [owner-onboarding-v1.md](owner-onboarding-v1.md).

## Staff and team (STEP 07)

`StaffMember` is an operational employee profile. `OrganizationMembership` remains the authorization source. `StaffPosition` is operational classification, not authorization.

See [staff-and-team-v1.md](staff-and-team-v1.md).

## Authorization

Named policies:

| Policy | Requirement |
|---|---|
| `OrganizationMember` | Authenticated + resolved organization + active membership + active organization |
| `OrganizationManager` | Above + membership role `Owner` or `Administrator` |

Policies revalidate membership server-side. They do not trust organization role claims embedded in cookies.

## Authentication foundation (v1)

- Local ASP.NET Core Identity with cookie authentication
- Public owner onboarding at `/onboarding` (STEP 06)
- `EmailConfirmed` remains `false` after onboarding; email confirmation provider/workflow is pre-commercial work
- No password reset in v1
- No OAuth / external providers in v1
- No MFA UX in v1

Cookie name: `WorkshopOS.Auth`
