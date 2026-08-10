# Security and Release Readiness v1

STEP 21 audit snapshot for WorkshopOS. This document records verified security posture, release gates, and remaining limitations.

## Authentication schemes

| Scheme | Cookie name | Purpose |
|--------|-------------|---------|
| ASP.NET Core Identity (default) | `WorkshopOS.Auth` | Staff workshop users |
| `WorkshopOS.CustomerPortal` | `WorkshopOS.CustomerPortal` | Estimate share portal sessions |

Schemes are isolated. Customer portal cookies carry only `share_public_id` and session kind claims — no customer PII, email, phone, or raw share token.

## Staff cookie security

- `HttpOnly = true`
- `SameSite = Lax`
- `SecurePolicy = Always` in non-Development; `SameAsRequest` in Development
- 8-hour sliding expiration
- Login path `/account/login`, access denied `/account/access-denied`

## Customer portal cookie security

- Separate scheme and cookie name from staff auth
- `HttpOnly = true`, `SameSite = Lax`, production `SecurePolicy = Always`
- Fixed 2-hour expiration, no sliding refresh
- DB share revalidation on each portal request via `CustomerPortalCookieEvents`
- Revoked/expired shares fail closed on revalidation

## Tenant isolation

- EF global query filters on tenant-scoped entities
- `OrganizationResolutionMiddleware` resolves active membership per request
- Unresolved organization context fails closed for tenant writes
- Composite FK constraints enforce cross-entity tenant consistency (23503/23505 regression suite)

## CSRF

- Cookie-authenticated POST mutations use `[ValidateAntiForgeryToken]` on MVC actions
- Portal token exchange POST includes antiforgery token
- No GET mutations on business routes

## Anonymous endpoint allowlist (8 endpoints)

| Controller | Endpoints |
|------------|-----------|
| `AccountController` | GET/POST login, GET access-denied |
| `OnboardingController` | GET/POST onboarding index |
| `PortalController` | GET access bootstrap, POST token exchange, GET access unavailable |

All other business modules require authenticated staff or portal scheme as appropriate.

## Login security

- Identity lockout: 5 failed attempts, 15-minute lockout
- Generic invalid-login message (no user enumeration by message)
- `returnUrl` validated with `Url.IsLocalUrl` — blocks open redirects
- **Staff login rate limit** (STEP 21): `StaffLogin` policy — 10 attempts / 5 minutes / IP, `QueueLimit = 0`

## Rate limiting

| Policy | Limit | Window |
|--------|-------|--------|
| `OwnerOnboarding` | 5 | 10 minutes |
| `CustomerPortalAccess` | 10 | 10 minutes |
| `StaffLogin` | 10 | 5 minutes |

Authenticated operational routes are not globally rate-limited.

## Production error handling

- Non-Development uses `UseExceptionHandler("/Home/Error")` and `UseHsts()`
- Error view shows request correlation ID only; development guidance hidden outside Development
- No stack traces, SQL, or connection strings in production error pages

## HTTPS / HSTS

- `UseHttpsRedirection()` enabled for all environments
- `UseHsts()` enabled only outside Development

## Security headers (STEP 21)

`SecurityHeadersMiddleware` applies baseline headers when not already set:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY` (when no CSP `frame-ancestors` present)
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: camera=(), microphone=(), geolocation=()`

Portal routes override with `Referrer-Policy: no-referrer`, `no-store` cache, and `frame-ancestors 'none'` CSP.

## CSP decision

**Deferred** for site-wide policy. Reasons:

- Portal bootstrap page requires inline script for hash-fragment token handling
- Public CSS imports Google Fonts (`fonts.googleapis.com`) via `@import`
- A restrictive CSP without `unsafe-inline` would require extracting portal bootstrap script and self-hosting fonts

Portal pages apply targeted `Content-Security-Policy: frame-ancestors 'none'` only.

## Estimate share secret design

- ≥256-bit RNG raw token (Base64Url, 43 characters)
- SHA-256 hash persisted; raw secret never stored
- Constant-time hash comparison
- URL form: `/portal/access/{PublicId}#token=SECRET`
- Secret never in GET query, path, cookie, TempData, or logs

## Private inspection media

- Storage under `App_Data/inspection-media` — validated outside `wwwroot` at startup
- Staff-authenticated, tenant-scoped content route
- Magic-byte validation; JPEG/PNG/WebP only; SVG rejected
- Limits: 8 MiB/photo, 6/item, 40/inspection
- Path traversal blocked; opaque server-generated storage keys only

## Financial / payment data boundary

- Workshop-entered payment ledger only
- No PAN, CVV, PIN, card expiry, or gateway card data
- Issued invoices and approved estimates immutable; payment ledger append-only

## Inventory invariants

- Negative stock blocked at DB/service layer
- No inventory mutation from estimates, portal approval, invoices, inspections, or repair order creation

## Reporting

- GET-only read paths
- `ReportingViewer` authorization policy
- Tenant-scoped; ≤366-day range; no multi-currency summing

## Logging / PII posture

- Do not log passwords, raw share tokens, connection strings, or full form bodies
- Prefer safe correlation IDs and entity identifiers

## STEP 20.1 hero security

`workshopos-hero.js` audit:

- No `innerHTML`, `eval`, `Function`, `fetch`, or `document.write`
- Decorative presentation only; no auth or business data
- `requestAnimationFrame` cancelled on `pagehide`
- `prefers-reduced-motion` disables parallax and idle motion (CSS + JS)

## Proxy / forwarded headers

- No trusted forwarded headers configured in application code
- **Production deployment requirement**: configure reverse proxy forwarded headers explicitly when behind load balancers; do not trust `X-Forwarded-For` blindly

## Configuration secrets

- `appsettings.json` contains no production credentials
- Local development uses User Secrets for `ConnectionStrings:WorkshopOS`
- Production must inject connection string via environment/secret store (values not committed)

## E2E golden paths (PostgreSQL, transaction rollback)

| Test | Coverage |
|------|----------|
| `EndToEnd_WorkshopCommercialLifecycle_CompletesSuccessfully` | Full portal-approval commercial lifecycle |
| `EndToEnd_StaffRecordedApproval_CommercialLifecycleCompletes` | Staff-recorded approval path |
| `EndToEnd_CrossTenantInvoiceAccess_IsDenied` | Cross-tenant IDOR negative |

Existing suites cover portal negatives, media security, share secrets, inventory, and constraint regressions.

## Release verification (macOS)

- Debug/Release build: 0 warnings, 0 errors
- Full test suite: 657/657 (620 STEP 21 baseline + 37 STEP 21–23)
- Release publish to `/tmp/workshopos-step21-publish`
- Published app GET smoke: public routes 200, protected routes challenge

## Windows portability

- Source audit: no hardcoded developer machine paths or platform-specific path concatenation in production code
- `Path.Combine` / `Path.GetFullPath` used for media storage
- Windows-targeted publish attempted in STEP 21 environment (see release report)

## Native Windows runtime

Not executed in STEP 21 macOS environment unless explicitly noted in release report.

## Authenticated browser visual QA

Pending unless safe pre-existing authenticated session available. No QA bypass or dev DB seeding performed.

## Remaining release blockers

None identified at STEP 21 completion when all gates pass.
