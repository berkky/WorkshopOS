# Customer Estimate Sharing & Portal v1 (STEP 16)

STEP 16 adds secure customer-facing estimate sharing, a narrowly scoped customer portal session, and direct customer approve/decline for presented estimates.

## Scope

- Workshop manager creates a secure estimate share (create / rotate / revoke)
- Cryptographically strong share secret (256-bit entropy, URL-safe Base64)
- Raw secret **never** persisted — only SHA-256 hash (64-char hex)
- Fragment-based share link: `/portal/access/{PublicId}#token={RAW_SECRET}`
- Separate `WorkshopOS.CustomerPortal` authentication scheme (not `WorkshopOS.Auth`)
- Customer portal session revalidated from database on every request
- Customer-safe estimate projection (no internal notes, staff data, or DVI media)
- Direct customer portal approve / decline for `Sent` estimates
- Immutable decision evidence on `EstimateShare` (distinguishes portal vs staff-recorded decisions)
- Share expiry (1–30 days, default 7), revocation, rotation with historical row retention

**Not in STEP 16:** customer password accounts, customer registration, general customer dashboard, email/SMS delivery, payment, invoice, PDF, digital signature, public inspection media, anonymous media URLs, customer file uploads.

## Customer ≠ ApplicationUser

`Customer` remains operational CRM data. STEP 16 does **not** convert customers into ASP.NET Identity users and does **not** add `Customer.UserId`.

Customer portal access is established by **possession of a cryptographically strong estimate share secret**, exchanged for a short-lived portal cookie session. This is **not** legal identity verification. Accurate wording:

- "customer portal share session"
- "customer decision submitted through the secure estimate share"

## EstimateShare entity

Tenant-owned `EstimateShare` represents a revocable, expiring access grant for exactly **one** `Estimate`.

| Field | Purpose |
|-------|---------|
| `PublicId` | Opaque, server-generated, globally unique — safe in URL, **not** a secret |
| `TokenHash` | SHA-256 hash of raw secret (unique index) |
| `ExpiresAtUtc` | Server-controlled expiry (bounded duration presets) |
| `RevokedAtUtc` / `RevokedByUserId` | Soft revocation audit |
| `Decision` / `DecisionAtUtc` | Direct portal approve/decline evidence |
| `LastAccessedAtUtc` | Updated on successful token exchange |
| `CreatedByUserId` | Server-derived from authenticated staff membership |

### One current share per estimate

PostgreSQL partial unique index:

```sql
UNIQUE (OrganizationId, EstimateId) WHERE RevokedAtUtc IS NULL
```

Historical revoked shares remain rows. Rotation revokes the current share and creates a new row in one transaction.

### Same-tenant constraints

- `(OrganizationId, EstimateId)` → `Estimate`
- `(OrganizationId, CreatedByUserId)` → `OrganizationMembership`
- `DeleteBehavior.Restrict` — no cross-tenant shares

## PublicId vs secret

| | PublicId | Raw secret |
|---|----------|------------|
| In URL path | Yes | **No** |
| In URL fragment | No | Yes (`#token=...`) |
| Sent to server on GET | Yes | **No** (fragment not sent) |
| Grants access alone | **No** | Yes (via POST exchange) |
| Persisted | Yes | **Never** |

Invalid or expired tokens return a generic message — no disclosure of whether `PublicId` exists.

## Share secret generation

- `RandomNumberGenerator.GetBytes(32)` — minimum 256 bits
- URL-safe Base64 encoding (`A-Za-z0-9-_`, no padding)
- Hash: SHA-256 → lowercase hex (64 chars)
- Comparison: `CryptographicOperations.FixedTimeEquals`

## Share eligibility

Managers (`EstimateManager`: Owner, Administrator, ServiceAdvisor) may create/rotate shares only when estimate status is:

| Status | Shareable | Portal actions |
|--------|-----------|----------------|
| `Draft` | **No** | — |
| `Sent` | Yes | Approve / Decline |
| `Approved` | Yes (read-only) | None |
| `Declined` | Yes (read-only) | None |

Share creation does **not** silently present a draft estimate.

## Expiry, revocation, rotation

- **Expiry** is access-control only — does not change estimate status
- **Revocation** invalidates raw link and existing portal sessions at next DB revalidation
- **Rotation** transactionally revokes current share + creates new share; raw new link shown **once** to staff
- Expired but non-revoked share blocks new sessions until rotated/revoked

## Fragment-based access bootstrap

```
GET  /portal/access/{publicId}     → bootstrap page (no secret on server)
POST /portal/access/{publicId}     → token exchange (antiforgery, rate-limited)
```

Bootstrap JavaScript:

1. Read token from `location.hash`
2. Clear fragment via `history.replaceState`
3. POST token in hidden form field

If JavaScript is disabled, user sees a clear message — no GET query-token fallback.

## CustomerPortal authentication

| Property | Value |
|----------|-------|
| Scheme | `WorkshopOS.CustomerPortal` |
| Cookie | `WorkshopOS.CustomerPortal` |
| HttpOnly | true |
| SameSite | Lax |
| SlidingExpiration | false |
| Absolute lifetime | ~2 hours |

Cookie principal claims (minimal):

- `EstimateSharePublicId`
- `CustomerPortalShareSession` marker

**Not in cookie:** raw token, customer PII, estimate totals, organization roles.

### DB revalidation

`CustomerPortalCookieEvents.OnValidatePrincipal` revalidates share on every request:

- Share exists, not revoked, not expired
- Estimate belongs to share and organization
- Estimate status is customer-visible

Revocation/expiry takes effect immediately without waiting for cookie expiry.

### Auth isolation

| Auth | Can access |
|------|------------|
| `WorkshopOS.Auth` (staff) | Internal routes (`/estimates`, `/customers`, …) |
| `WorkshopOS.CustomerPortal` | `/portal/*` only |

Staff cookie does **not** authorize portal routes. Portal cookie does **not** authorize staff routes or `OrganizationMember` / manager policies.

## Portal routes

| Method | Route | Auth |
|--------|-------|------|
| GET | `/portal/access/{publicId}` | Anonymous |
| POST | `/portal/access/{publicId}` | Anonymous + rate limit |
| GET | `/portal/estimate` | CustomerPortal |
| POST | `/portal/estimate/approve` | CustomerPortal |
| POST | `/portal/estimate/decline` | CustomerPortal |
| POST | `/portal/logout` | CustomerPortal |

Portal session is share-scoped — no client-supplied `estimateId`, `customerId`, or `organizationId`.

Customer portal does not expose inventory quantities, stock movements, catalog management data, invoices, payment state, or payment history.

## Customer-safe projection

Portal shows: organization display name, estimate number/status, repair order number, vehicle make/model/year/plate, customer message, line items (description, quantity, unit price, line total), currency, total, presented/decision timestamps.

**Not exposed:** internal repair order notes, inspection technician notes, inspection item notes, `InspectionMediaAsset`, staff/membership data, database IDs, storage keys, hashes.

## Direct customer decisions

For `Sent` estimates with valid portal session:

- **Approve:** `Estimate.RecordCustomerApproval` + `EstimateShare.Decision = Approved` (atomic, serializable transaction)
- **Decline:** `Estimate.RecordCustomerDecline` + `EstimateShare.Decision = Declined` (atomic)

Portal decision does **not** mutate repair order, appointment, technician work status, inspection, inspection items, or media.

Staff-recorded approval/decline (STEP 15) remains valid. Internal UI distinguishes:

- "Customer portal approval/decline" — when `EstimateShare.Decision` is set
- "Approval recorded by workshop staff" — when estimate is terminal but share decision is null

## Rate limiting

Anonymous token exchange: `CustomerPortalAccess` policy — 10 attempts / 10 minutes per `RemoteIpAddress`, queue limit 0. Defense-in-depth; security relies on 256-bit entropy, not rate limits alone.

Behind a trusted reverse proxy, `RemoteIpAddress` partitioning must be revisited with forwarded-headers configuration.

## HTTP security (portal)

- `Cache-Control: private, no-store`
- `Referrer-Policy: no-referrer`
- `X-Content-Type-Options: nosniff`
- Clickjacking protection (`frame-ancestors 'none'` or `X-Frame-Options: DENY`)

## Internal staff UI

Estimate details include "Share with Customer" section:

- Share status (Active / Expired / Revoked)
- Created / expires / last accessed timestamps
- Portal decision source when applicable
- Actions: Create / Rotate / Revoke secure link
- One-time raw link display on create/rotate (not in TempData, session, or logs)

## Independence guarantees

Customer portal decisions must not mutate:

- `RepairOrder` status / priority / internal notes
- `RepairOrderTechnicianAssignment.WorkStatus`
- `Appointment.Status`
- `Inspection` / `InspectionItem` / `InspectionMediaAsset`

## Compliance note

STEP 16 provides possession-based estimate review and decision capture. It does not provide invoice, payment, legally binding digital signature, or verified legal identity. Customer portal routes do not expose management reporting (STEP 19).
