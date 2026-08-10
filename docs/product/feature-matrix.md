# Feature Matrix

WorkshopOS v1.0.0-rc1 — accurate product capability classification.

## AVAILABLE (implemented in source)

| Area | Capability |
|------|------------|
| Tenancy | Multi-tenant organizations with membership roles |
| Identity | Staff authentication (ASP.NET Core Identity) |
| Onboarding | Owner self-service organization creation |
| CRM | Customers |
| Vehicles | Vehicle records linked to customers |
| Appointments | Scheduling and calendar views |
| Repair orders | Walk-in and appointment-linked jobs |
| Operations | Technician assignment, priority, operations board |
| Technician work | My Jobs start/complete work status |
| DVI | Structured inspection checklists and lifecycle |
| Inspection media | Private JPEG/PNG/WebP photo evidence |
| Estimates | Draft lines, present, staff approval/decline |
| Estimate sharing | Secure share links with expiry/revocation |
| Customer portal | Estimate view and approve/decline (share-scoped) |
| Catalog | Service and parts catalog |
| Inventory | Location balances and immutable movement ledger |
| Invoicing | Issue invoices from approved estimates |
| Payments | Workshop-entered payment ledger (metadata) |
| Commercial close | Repair order commercial closure |
| Reporting | Read-only dashboard and KPI reports |
| UX | Premium design system with 3D motion hero (public landing) |

## DEPLOYMENT CONFIGURATION REQUIRED

| Item | Notes |
|------|-------|
| Production PostgreSQL | External database |
| HTTPS / public hostname | TLS termination |
| Trusted reverse proxy | Forwarded headers for scheme/IP |
| Data Protection key ring | Persistent keys for auth cookies |
| Private media storage | Persistent path outside `wwwroot` |
| Database backups | Operator responsibility |
| Secret management | Connection strings, keys |
| Explicit migrations | Not at application startup |

## DEFERRED / FUTURE (not in v1.0.0-rc1)

| Item | Notes |
|------|-------|
| Payment gateway | Card processing, PCI scope |
| Email / SMS delivery | Share link delivery automation |
| Cloud media storage | S3/Azure Blob abstraction |
| Tax / VAT / e-invoice | Compliance engines |
| Supplier purchasing | Purchase orders |
| General customer accounts | Portal is estimate-share scoped only |
| DVI media customer sharing | Staff-only media today |
| AI diagnostics | Not implemented |
| Global CSP with nonces | Deferred hardening |
| Native Windows runtime verification | Source portable; runtime not tested |

## Verification status

| Item | Status |
|------|--------|
| macOS build/test/publish | Verified |
| Windows native runtime | Not verified |
| Independent browser viewport screenshots | Not captured (tooling limitation) |
| Project license terms | **Proprietary direction chosen** — final legal instrument **pending** |
