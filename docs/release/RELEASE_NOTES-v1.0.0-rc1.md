# Release Notes — WorkshopOS v1.0.0-rc1

**Release candidate label:** WorkshopOS v1.0.0-rc1  
**Technical RC date:** August 2026  
**Migration count:** 9

## Overview

WorkshopOS is a premium automotive workshop management platform covering customer CRM, appointments, repair orders, technician operations, digital vehicle inspection, estimates, secure customer portal approval, catalog/inventory, invoicing, payments, and read-only reporting.

## Major capabilities

- Multi-tenant organization model with staff identity and role-based authorization
- Repair order lifecycle with technician assignment and operations board
- Digital Vehicle Inspection (DVI) with immutable completed records
- Private inspection photo evidence (JPEG/PNG/WebP)
- Commercial estimates with staff-recorded and customer-portal approval paths
- Secure estimate sharing (fragment token bootstrap, hash-only persistence)
- Service/parts catalog with location inventory ledger
- Issued invoices, payment ledger, and commercial job close
- Read-only dashboard and operational/commercial reports
- Premium design system with cinematic 3D motion hero (STEP 20.1)

## Security baseline (STEP 21)

- Separate staff and Customer Portal authentication schemes
- CSRF on all cookie-auth mutations
- Login, onboarding, and portal exchange rate limiting
- Baseline security headers; portal-specific cache/referrer/CSP frame protection
- Tenant isolation with composite foreign keys and write guards
- Private media outside `wwwroot`
- 657 automated regression tests including E2E commercial lifecycle paths

## Regression baseline

| Metric | Value |
|--------|-------|
| Tests | 684 pass at STEP 29 verification |
| Build | Debug/Release/warnaserror 0 errors, 0 warnings |
| Vulnerable NuGet packages | None (advisory scan) |
| Pending model changes | No |
| Health endpoints | `/health/live`, `/health/ready` |
| Repair orders list | Server-side pagination (UI + service) |

## Material improvements since initial RC packaging (STEP 23)

- Premium authenticated staff UI (workspace headers, pagination, related modules, mobile list cards)
- Premium form system (`wos-form-shell`, shared validation summary, catalog form partials)
- Cross-platform release engineering proof (macOS native; Windows `win-x64` cross-publish)
- Repair orders server-side pagination with filter-preserving UI
- Liveness and readiness health endpoints with privacy-safe responses
- Portability contract tests and deployment guides (macOS/Windows)
- Windows native validation harness (`scripts/release/Test-WorkshopOS-Windows.ps1`)
- Authenticated real-browser premium staff visual QA on disposable `workshopos_test` dataset (STEP 29)

## Known RC operational verification items

- Native Windows runtime **not yet verified** (harness ready for trusted runner)
- Final commercial brand clearance pending
- Final proprietary LICENSE pending legal review
- Canonical release commit/tag not authorized

## Upgrade / migration

Fresh install requires explicit `dotnet ef database update`. Application startup does not migrate.

## Documentation

See `docs/setup/`, `docs/deployment/`, `docs/demo/`, and `docs/architecture/README.md`.
