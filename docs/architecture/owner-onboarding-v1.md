# WorkshopOS Owner Onboarding v1

## Scope

STEP 06 introduces secure **owner onboarding** for anonymous users who become organization owners with a first active workshop location. This is not public staff registration and not customer self-service.

Out of scope for STEP 06:

- email confirmation provider/workflow
- password reset
- OAuth / external identity
- MFA
- subscription, billing, payments
- staff/technician domain (STEP 07)

## Global identity preserved

`ApplicationUser` remains a **global** login identity. There is still **no** `ApplicationUser.OrganizationId`.

Organization access is established through `OrganizationMembership` with organization-specific roles.

## Owner provisioning flow

```text
Anonymous GET /onboarding
→ POST /onboarding (antiforgery + rate limit)
→ validate/normalize input server-side
→ single PostgreSQL transaction:
      ApplicationUser (EmailConfirmed = false)
      Organization (server-generated slug, Active)
      OrganizationMembership (Owner, Active)
      WorkshopLocation (first location, IsActive = true)
→ commit
→ SignInManager.SignInAsync (only after commit)
→ GET /onboarding/complete (OrganizationMember policy)
```

Client input must **not** control:

- `UserId`
- `OrganizationId`
- `MembershipId`
- `WorkshopLocationId`
- organization status
- membership role/status
- location `IsActive`
- `EmailConfirmed`
- organization slug

## Atomicity

Owner onboarding is atomic across:

- `ApplicationUser`
- `Organization`
- `OrganizationMembership`
- `WorkshopLocation`

`UserManager.CreateAsync` participates in the same `AppDbContext` PostgreSQL transaction as tenant workspace provisioning. Integration tests prove rollback when a production continuation seam (`IOwnerOnboardingPostIdentityGate`) fails after Identity user creation.

The tenant write guard is **not** bypassed. The first `WorkshopLocation` is written only after trusted membership validation and scoped organization resolution for the newly created organization.

## Input validation

| Input | Rule |
|---|---|
| Currency | Three letters; normalized to uppercase (example: `eur` → `EUR`) |
| Time zone | IANA-oriented validation via `TimeZoneInfo.FindSystemTimeZoneById` |
| Organization slug | Server-generated, normalized, URL-safe, unique suffix from organization id |
| Email | Normalized trim; `EmailConfirmed` remains `false` |

Duplicate email returns a generic public failure without exposing raw Identity/database details.

## Public onboarding security

| Route | Protection |
|---|---|
| `GET /onboarding` | Anonymous |
| `POST /onboarding` | `ValidateAntiForgeryToken`, rate limit policy `OwnerOnboarding` (5 attempts / 10 minutes, `QueueLimit = 0`, `RemoteIpAddress` partition) |
| `GET /onboarding/complete` | `OrganizationMember` authorization |

Rate limiting applies only to owner onboarding POST — not the whole site.

**Production note:** `RemoteIpAddress` partitioning must be revisited behind a trusted reverse proxy so the partition key reflects the real client IP.

## Multi-organization selection foundation

Authenticated routes:

- `GET /organization/select`
- `POST /organization/select` (antiforgery)

Requested `OrganizationId` is untrusted client input. Selection validates authenticated `UserId`, active membership, and active organization before issuing a `WorkshopOS.SelectedOrganization` claim hint.

## Selected organization claim is not authority

`WorkshopOS.SelectedOrganization` is a **hint only**. `OrganizationResolutionMiddleware` revalidates membership and organization status from the database on every request.

Invalid, suspended, revoked, or inactive selections fail closed to unresolved context even if an old cookie still contains the claim.

## Email confirmation status

`EmailConfirmed` remains `false` after owner onboarding. Email confirmation provider/workflow remains pre-commercial work.

## Production readiness

STEP 06 is a foundation slice. It is not production-ready onboarding for commercial launch without email confirmation, hardened rate-limit partitioning behind a reverse proxy, and operational monitoring.
