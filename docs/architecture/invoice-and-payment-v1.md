# Invoice & Payment v1 (STEP 18)

STEP 18 introduces workshop billing: draft/issue invoices, payment recording, and commercial close for repair orders.

## Scope

- Create draft invoice for eligible repair order (blank or from approved estimate snapshot)
- Server-generated invoice number (`INV-{yyyyMMdd}-{suffix}`)
- Currency snapshot from organization (blank) or approved estimate (estimate path)
- Generic priced line items with server-computed totals
- Issue invoice (`Draft` → `Issued`, requires ≥1 item)
- Record payments against issued invoices (partial or full)
- Void draft invoices; void issued only when zero payments
- Commercial close on completed repair order when current issued invoice is fully paid
- `RepairOrder.CommerciallyClosedAtUtc` marks commercial completion

**Not in STEP 18:** tax engine, PDF, accounting export, inventory consumption, estimate mutation, appointment/DVI side effects, customer portal billing.

## Roles

`BillingManager` policy: Owner, Administrator, ServiceAdvisor.

Technicians and viewers may list/view invoices as organization members but cannot mutate billing.

## Uniqueness

Partial unique indexes (PostgreSQL):

- One non-voided invoice per `(OrganizationId, RepairOrderId)`
- One non-voided invoice per `(OrganizationId, SourceEstimateId)` when set

## Lifecycle

| Invoice status | Meaning |
|----------------|---------|
| `Draft` | Editable line items and commercial notes |
| `Issued` | Accepts payments; items immutable |
| `Voided` | Terminal |

Commercial close requires:

1. Repair order `Completed`
2. Current non-voided invoice `Issued` and fully paid
3. Not already commercially closed

## Money

Same rounding rules as estimates: line totals rounded per item, invoice total is sum of line totals. Payments use `Serializable` transactions with overpayment guards.

## Tenant ownership

- `Invoice`, `InvoiceItem`, `InvoicePaymentRecord` are organization-owned with global query filters
- Composite FKs to `RepairOrder`, `Estimate`, and `OrganizationMembership` (payment recorder)

## Web

- `/invoices` list and detail/edit flows
- Repair order details: billing summary, create invoice, commercial close
- Approved estimate details: create invoice from estimate

See also [estimate-and-approval-v1.md](estimate-and-approval-v1.md) and [commercial-boundaries-v1.md](commercial-boundaries-v1.md).

## Reporting (STEP 19)

Invoice and payment KPIs in management reports are read-only projections: issued invoices (`IssuedAtUtc`), recorded payments (`RecordedAtUtc`), outstanding balances by currency, and derived payment state. No billing mutations from reporting routes. See [reporting-and-analytics-v1.md](reporting-and-analytics-v1.md).
