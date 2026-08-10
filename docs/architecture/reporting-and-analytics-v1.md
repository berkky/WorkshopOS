# Reporting and Analytics v1

STEP 19 introduces read-only management reporting derived from authoritative transactional data. No analytics persistence, cache tables, background jobs, or schema changes.

## Architecture

```
Authoritative transactional tables (RepairOrder, Appointment, Inspection, Estimate, Invoice, …)
    → IWorkshopReportingService / WorkshopReportingService (read-only EF projections)
    → GET /dashboard, GET /reports/operations, GET /reports/commercial
```

Reporting never mutates domain state.

## Authorization

Policy: `ReportingViewer` (`ReportingViewerPolicy`).

| Role | Access |
|------|--------|
| Owner | Allowed |
| Administrator | Allowed |
| ServiceAdvisor | Allowed |
| Technician | Denied |
| Viewer | Denied |

Authorization uses `OrganizationMembership.Role` (not ASP.NET Identity roles). MVC policy and service entry points both DB-revalidate membership. Role changes take effect on the next request.

Customer portal cannot access reporting routes or metrics.

## Tenant and location isolation

- All queries run within `IOrganizationContext`; `OrganizationId` is never accepted from query/form.
- Unresolved organization fails closed.
- Optional `WorkshopLocationId` is validated against the current organization (cross-tenant IDs return not found).
- Historical/inactive locations remain valid for filtering when they belong to the tenant.

## Date range contract

- Input: `DateOnly From`, `DateOnly To` (GET query).
- Default: last 30 calendar days including today (business-local).
- Maximum inclusive span: 366 days.
- Rejects `From > To` and spans over 366 days.
- No unbounded “all history” in v1.

## Timezone

- Report dates are business-local calendar days.
- Effective timezone: selected `WorkshopLocation.TimeZoneId` when set, otherwise `Organization.TimeZoneId`.
- Conversion: local `From 00:00` through local day after `To 00:00` → UTC half-open interval `[startUtc, endUtcExclusive)` via BCL `TimeZoneInfo`.
- UI shows selected range, effective timezone, location scope, and generated timestamp.

## Current snapshot vs selected period

| Kind | Examples | Period filter |
|------|----------|---------------|
| Current snapshot | Active repair orders, outstanding invoices, open DVI | No |
| Selected period | Appointments scheduled, RO opened/completed, recorded payments | Yes |

Labels and UI sections keep these semantics distinct.

## Metric sources

| Metric | Source |
|--------|--------|
| Active repair orders | `RepairOrder.Status` not `Completed`/`Cancelled` |
| Unassigned | Active RO without assignment where `UnassignedAtUtc == null` |
| Urgent | `RepairOrderPriority.Urgent` among active ROs |
| Technicians working | Active assignments with `TechnicianWorkStatus.InProgress` on non-terminal RO |
| Open DVI | `InspectionStatus` `Draft` or `InProgress` |
| Estimates awaiting decision | `EstimateStatus.Sent` |
| Outstanding invoices | `InvoiceStatus.Issued`, `AmountPaid < total` |
| Commercially closable | `Completed` RO, `CommerciallyClosedAtUtc == null`, issued non-voided invoice fully paid |
| Appointments in period | `Appointment.ScheduledStartUtc` in UTC range |
| RO opened | `RepairOrder.OpenedAtUtc` |
| RO completed | `RepairOrder.CompletedAtUtc` |
| Commercial close | `RepairOrder.CommerciallyClosedAtUtc` |
| Inspections completed | `Inspection.CompletedAtUtc` |
| Critical findings | `InspectionItem.Condition == Critical` on inspections completed in period |
| Estimates presented | `Estimate.SentAtUtc` |
| Approved / declined | `ApprovedAtUtc` / `DeclinedAtUtc` |
| Decision approval rate | `approved / (approved + declined)` in period; `—` if denominator 0 |
| Portal vs staff decisions | `EstimateShare.Decision` / `DecisionAtUtc` evidence |
| Invoices issued | `Invoice.IssuedAtUtc` |
| Recorded payments | `InvoicePaymentRecord.RecordedAtUtc` (workshop-entered ledger, not accounting revenue) |

## Multi-currency

Amounts are grouped by `CurrencyCode`. Different currencies are never summed together. No FX conversion.

Money totals use `InvoiceMoneyCalculator` rounding semantics.

## Out of scope (v1)

- Profit, margin, inventory valuation, tax reports
- CSV/Excel/PDF export, chart libraries, trend deltas
- Analytics persistence, caches, background aggregation
- Employee performance scores or rankings

## Performance

- `AsNoTracking` projections; set-based queries; no N+1 per entity.
- Several focused queries per report are acceptable; correctness over single-query elegance.

## Routes

| Route | Policy |
|-------|--------|
| `GET /dashboard` | `ReportingViewer` |
| `GET /reports/operations` | `ReportingViewer` |
| `GET /reports/commercial` | `ReportingViewer` |

Responses use `Cache-Control: private, no-store`.
