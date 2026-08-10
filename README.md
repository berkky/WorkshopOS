# WorkshopOS

**WorkshopOS v1.0.0-rc1** — Premium automotive workshop management SaaS.

End-to-end workshop operations: customer CRM, appointments, repair orders, technician workflow, digital vehicle inspection (DVI) with private photo evidence, estimates, secure customer portal approval, catalog/inventory, invoicing, payment ledger, and read-only executive reporting.

## Release candidate status

| Item | Status |
|------|--------|
| Technical release candidate | **READY** (source verified) |
| Production deployment | **Not deployed** — see [deployment runbook](docs/deployment/deployment-runbook.md) |
| Project license | **Proprietary direction chosen** — final legal instrument **pending** |
| **Commercial brand** | **WorkshopOS = working name; NOT commercially cleared** — see [brand-governance.md](docs/product/brand-governance.md) |
| Tests (RC baseline) | 657 pass |
| Migrations | 9 applied schema; startup does not auto-migrate |

## Quick start

```bash
dotnet tool restore
dotnet restore WorkshopOS.slnx
# Configure User Secrets — see docs/setup/local-development.md
dotnet ef database update --project src/WorkshopOS.Infrastructure --startup-project src/WorkshopOS.Infrastructure
dotnet build WorkshopOS.slnx
dotnet test WorkshopOS.slnx
dotnet run --project src/WorkshopOS.Web
```

First owner: open `/onboarding`. No demo seed at startup.

## Requirements

- [.NET SDK 10.0.301](global.json)
- PostgreSQL (18.x tested; see [prerequisites](docs/setup/prerequisites.md))
- Modern browser

No Node.js/npm required.

## Feature overview

| Module | Capability |
|--------|------------|
| CRM & vehicles | Customers, vehicles, history |
| Scheduling | Appointments and calendar |
| Repair orders | Intake, lifecycle, commercial close |
| Operations | Technician assignment, priority queue |
| DVI | Structured inspections + private photos |
| Estimates | Present, staff or portal approval |
| Customer portal | Secure share-link approve/decline |
| Catalog & inventory | Services, parts, movement ledger |
| Billing | Invoices, payment records (ledger) |
| Reporting | Read-only dashboard and KPIs |

Full matrix: [docs/product/feature-matrix.md](docs/product/feature-matrix.md)  
Known limitations: [docs/product/current-limitations.md](docs/product/current-limitations.md)

## Architecture

Layered .NET 10 solution:

| Project | Role |
|---------|------|
| `WorkshopOS.Domain` | Entities and domain rules |
| `WorkshopOS.Application` | Application services and policies |
| `WorkshopOS.Infrastructure` | EF Core, Identity, PostgreSQL |
| `WorkshopOS.Web` | ASP.NET Core MVC UI |

Multi-tenant isolation via organization-scoped queries and composite foreign keys. Separate staff (`WorkshopOS.Auth`) and customer portal (`WorkshopOS.CustomerPortal`) authentication schemes.

Index: [docs/architecture/README.md](docs/architecture/README.md)

## Security model (summary)

- Tenant isolation on all business resources
- CSRF on all cookie-auth mutations
- Rate limiting: login, onboarding, portal exchange
- Private inspection media outside `wwwroot`
- Estimate share secrets: fragment bootstrap, SHA-256 hash only
- Baseline security headers; portal-specific cache/referrer controls

Details: [docs/architecture/security-and-release-readiness-v1.md](docs/architecture/security-and-release-readiness-v1.md)  
Deployment: [docs/deployment/security-checklist.md](docs/deployment/security-checklist.md)

## Configuration

| Project | Key |
|---------|-----|
| `WorkshopOS.Web` | `ConnectionStrings:WorkshopOS` |
| `WorkshopOS.Infrastructure.IntegrationTests` | `ConnectionStrings:WorkshopOSTest` |

Never commit credentials. Use User Secrets locally; secret manager in production.

## Database

| Database | Purpose |
|----------|---------|
| `workshopos_dev` | Development |
| `workshopos_test` | Integration tests (transaction rollback) |

Migrations: [docs/setup/database-migrations.md](docs/setup/database-migrations.md)  
PostgreSQL setup: [docs/development/local-postgresql.md](docs/development/local-postgresql.md)

## Testing

```bash
dotnet test WorkshopOS.slnx
```

Guide: [docs/setup/testing.md](docs/setup/testing.md)

## Documentation index

| Area | Path |
|------|------|
| Prerequisites | [docs/setup/prerequisites.md](docs/setup/prerequisites.md) |
| Local development | [docs/setup/local-development.md](docs/setup/local-development.md) |
| Production config | [docs/deployment/production-configuration.md](docs/deployment/production-configuration.md) |
| Deployment runbook | [docs/deployment/deployment-runbook.md](docs/deployment/deployment-runbook.md) |
| Demo runbook | [docs/demo/demo-runbook.md](docs/demo/demo-runbook.md) |
| Release governance | [docs/release/release-governance.md](docs/release/release-governance.md) |
| Commercial term sheet | [docs/release/proprietary-commercial-license-term-sheet.md](docs/release/proprietary-commercial-license-term-sheet.md) |
| Legal review pack | [docs/release/legal-review-pack.md](docs/release/legal-review-pack.md) |
| Buyer FAQ | [docs/product/commercial-buyer-faq.md](docs/product/commercial-buyer-faq.md) |
| Source package manifest | [docs/release/source-package-manifest.md](docs/release/source-package-manifest.md) |
| Third-party notices | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) |
| Security policy | [SECURITY.md](SECURITY.md) |

## Cross-platform

| Platform | Status |
|----------|--------|
| macOS | Build, test, publish, runtime smoke verified |
| Windows source | Path portability verified |
| Windows native runtime | Not executed in RC verification |

## Design

Premium dark luxury-tech aesthetic with layered glass UI, automotive wireframe identity, and restrained 3D motion hero on the public landing page. See [design-system-and-ux-v1.md](docs/architecture/design-system-and-ux-v1.md).

## License

**Owner direction:** WorkshopOS is intended for distribution under a **Proprietary Commercial Source License** (WorkshopOS Commercial Source Edition).

A **final legal LICENSE / agreement is not yet published** in this repository. Governance drafts for counsel review:

- [Commercial term sheet](docs/release/proprietary-commercial-license-term-sheet.md)
- [License decision matrix](docs/release/license-decision-matrix.md)
- [Legal review pack](docs/release/legal-review-pack.md)

Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). This is **not** COMMERCIAL DISTRIBUTION READY until final legal terms and security contact are closed.

## Source control note

Technical RC content exists in the **working tree** (STEPS 04–22). It is not yet represented by a release commit or tag. Commit/tag requires separate owner approval.
