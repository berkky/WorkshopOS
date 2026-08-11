# Manual Visual QA Checklist

**Status: AUTHENTICATED WALKTHROUGH COMPLETE (STEP 29)**

Authenticated real-browser walkthrough completed on **2026-08-10** against **`workshopos_test`** using a disposable synthetic owner account (`qa-step29-*@local.invalid`), normal `/account/login` cookie auth, and mandatory post-QA cleanup (baseline counts restored). No auth bypass. No writes to `workshopos_dev`.

## STEP 29 evidence summary

| Item | Result |
|------|--------|
| Database | `workshopos_test` only (safety gate PASS) |
| Viewports | Desktop **1440×1000**, mobile **390×844** |
| Login | Real ASP.NET Identity password validation |
| Console errors | 0 unexpected |
| Page errors | 0 unexpected |
| First-party request failures | 0 unexpected |
| Logout | POST sign-out; `/dashboard` redirects to login |
| Screenshots | `.visual-qa/step29/` (gitignored) |
| QA data cleanup | Targeted delete by organization ID; `VERIFY_PASS` |

### Desktop routes captured

`01-dashboard` through `14-team` (dashboard, customers, customer detail, vehicles, appointment calendar, repair orders, repair-order detail, operations, DVI detail, estimate detail, inventory, invoice detail, operations report, team).

### Mobile routes captured

Dashboard, appointment calendar, repair-order detail, DVI detail, invoice detail, customer create form.

## Preconditions

- Isolated demo database (not `workshopos_dev` production data)
- Owner or staff credentials via normal login — **no auth bypass**
- Do not seed QA-specific data into shared development databases
- Viewports: **1440×1000** (desktop) and **390×844** (mobile)

## Public pages (unauthenticated)

| Route | Desktop | Mobile | Notes |
|-------|---------|--------|-------|
| `/` | [ ] | [ ] | Hero, wireframe vehicle, CTAs, no horizontal overflow |
| `/account/login` | [ ] | [ ] | Form layout, branding, responsive shell |
| `/onboarding` | [ ] | [ ] | Form layout (if accessible pre-auth) |
| `/health/live` | [ ] | — | 200, minimal JSON (infrastructure only) |

### Public motion and assets

- [ ] Pointer parallax restrained on desktop
- [ ] `prefers-reduced-motion: reduce` → static layout
- [ ] Local DM Sans / design CSS load (no CDN fonts)
- [ ] `workshopos-hero.js` and hero SVG load (200)

## Authenticated staff walkthrough

Log in through `/account/login` with a demo owner account. Complete organization selection if prompted.

| Route | Desktop 1440×1000 | Mobile 390×844 | Checks |
|-------|-------------------|----------------|--------|
| `/dashboard` | [ ] | [ ] | Shell, sidebar, KPI cards, no internal nav on public pages |
| `/customers` | [ ] | [ ] | List, filters, pagination, premium table/cards |
| `/customers` (detail) | [ ] | [ ] | Workspace header, related modules, status badges |
| `/vehicles` | [ ] | [ ] | List, pagination, mobile cards |
| `/appointments` | [ ] | [ ] | List, filters, pagination |
| `/appointments/calendar` | [ ] | [ ] | Calendar grid, events readable |
| `/repair-orders` | [ ] | [ ] | Server-side pagination, filters, mobile list |
| `/operations` | [ ] | [ ] | Operations board layout |
| `/inspections` | [ ] | [ ] | DVI list and detail patterns |
| `/inspections` (detail) | [ ] | [ ] | Checklist, photo UI (staff-only media) |
| `/estimates` | [ ] | [ ] | List, detail, share-created flow |
| `/inventory` | [ ] | [ ] | Stock list, adjust/history forms |
| `/invoices` | [ ] | [ ] | List, detail, payment UI |
| `/reports` (operations/commercial) | [ ] | [ ] | Read-only reporting tables |
| `/team` | [ ] | [ ] | Staff list, roles, forms |

### Authenticated UX contracts

- [ ] `wos-app` shell consistent across modules
- [ ] `_DetailWorkspaceHeader` on detail pages
- [ ] `_Pagination` on paginated lists (customers, vehicles, appointments, repair orders)
- [ ] `_FormValidationSummary` on create/edit forms
- [ ] `_StatusBadge` on status columns
- [ ] No `display-6` Bootstrap headings in views
- [ ] No horizontal overflow at 390px width

## Customer portal (share link)

| Area | Checks |
|------|--------|
| Portal bootstrap | Spinner → exchange → estimate view |
| Portal estimate | Read-only when approved; approve/decline when sent |
| Isolation | No staff shell, no inspection media URLs |

## Health (infrastructure)

| Route | Expected |
|-------|----------|
| `/health/live` | 200, no secrets in body |
| `/health/ready` | 200 when DB reachable (optional in UI QA) |

## Policy

- Record pass/fail per route and viewport
- Attach screenshots to release evidence when available
- Do **not** modify `workshopos_dev` credentials or seed auth bypass users
- This checklist is **manual operational QA**, not automated proof

## Blocker status

| Item | Status |
|------|--------|
| Authenticated real-browser walkthrough | **PASS (STEP 29)** |
| Auth bypass for QA | **Not permitted** |
