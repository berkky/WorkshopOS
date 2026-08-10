# Estimate & Customer Approval v1 (STEP 15)

STEP 15 introduces a commercial estimate foundation with staff-recorded customer approval workflow for WorkshopOS repair orders.

## Scope

- Create estimate for eligible repair order
- Server-generated estimate number (`EST-{yyyyMMdd}-{suffix}`)
- Currency snapshot from organization at creation
- Generic priced line items (description, quantity, unit price)
- Server-computed line and estimate totals
- Draft editing (add/edit/remove items, customer message)
- Present for approval (`Draft` → `Sent`)
- Staff-recorded customer approval (`Sent` → `Approved`)
- Staff-recorded customer decline (`Sent` → `Declined`)
- Terminal immutability after approval/decline
- Multiple historical estimates per repair order
- Inspection finding context on draft/details (read-only; no auto-pricing)

**Not in STEP 15:** public approval URLs, customer portal, email/SMS, invoice, payment, tax engine, PDF, digital signature, automatic DVI→estimate conversion, parts inventory.

**Added in STEP 16:** secure customer share links, customer portal sessions, and direct customer portal approve/decline — see [customer-estimate-sharing-v1.md](customer-estimate-sharing-v1.md).

**Added in STEP 17:** service/parts catalog and estimate catalog snapshot helpers — see [catalog-and-inventory-v1.md](catalog-and-inventory-v1.md). Catalog selection does not reserve or consume stock.

**Added in STEP 18:** invoices, payments, and commercial close — see [invoice-and-payment-v1.md](invoice-and-payment-v1.md). Billing does not mutate estimates, inventory, or repair execution state beyond `CommerciallyClosedAtUtc`.

## Estimate vs RepairOrder

`Estimate` is a **commercial record** linked to a `RepairOrder`. It does **not** own repair execution.

| Concern | Estimate | RepairOrder |
|--------|----------|-------------|
| Create | Manager on eligible RO | Manager intake |
| Present / approve / decline | Estimate lifecycle only | Unaffected |
| Technician work | Unaffected | Assignment / work status |
| DVI | Read-only context | Unaffected |

Present, approve, and decline **do not** start, complete, or cancel repair orders.

## Tenant ownership

- `Estimate` and `EstimateItem` are organization-owned
- `OrganizationId` from `IOrganizationContext` only (never client-supplied)
- Composite FKs:
  - `(OrganizationId, RepairOrderId)` → `RepairOrder`
  - `(OrganizationId, EstimateId)` → `Estimate`

## Currency snapshot

`Estimate.CurrencyCode` is set from `Organization.DefaultCurrencyCode` at creation. Later organization currency changes do not rewrite historical estimates.

## Money calculation

- `UnitPrice`: `numeric(18,2)`
- `Quantity`: `numeric(12,3)`
- Line total: `decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero)`
- Estimate total: sum of rounded line totals
- Client-submitted totals are never authoritative

Zero unit price is allowed (warranty/complimentary lines). Quantity must be > 0. Unit price cannot be negative.

Maximum **100** line items per estimate (server-enforced technical limit).

## Estimate status lifecycle

Uses existing `EstimateStatus` enum:

| Status | STEP 15 role |
|--------|----------------|
| `Draft` | Editable; can present when ≥1 item |
| `Sent` | Presented for customer decision; financial contents immutable |
| `Approved` | Terminal; staff-recorded customer approval |
| `Declined` | Terminal; staff-recorded customer decline |

`PartiallyApproved`, `Expired`, `Superseded` exist in schema but are not used in STEP 15 workflows.

Timestamps (migration 6): `SentAtUtc`, `ApprovedAtUtc`, `DeclinedAtUtc`.

## Customer decisions

### Staff-recorded (STEP 15)

STEP 15 records customer decisions **through authorized workshop staff** in internal UI:

- "Record customer approval"
- "Record customer decline"

This is **not** authenticated customer action. UI must not imply digital signature or direct customer login.

### Customer portal (STEP 16)

Customers with a valid secure share link may approve or decline `Sent` estimates through the customer portal. Portal decisions are recorded on `EstimateShare.Decision` and are distinguishable from staff-recorded decisions. See [customer-estimate-sharing-v1.md](customer-estimate-sharing-v1.md).

## Draft editability

While `Draft`:

- Add, edit, remove line items
- Edit customer message

Once presented (`Sent`) or terminal (`Approved`/`Declined`), financial contents are immutable. No hard delete of estimates or historical lines after presentation.

## Repair recommendations / DVI

Latest inspection findings (`Attention`, `Critical`, `Monitor`) may display as read-only context when editing/viewing estimates. Advisors manually create `EstimateItem` rows. No automatic conversion of inspection conditions to priced lines. No `InspectionItemId` on estimate items.

## Authorization

### EstimateManager (ASP.NET policy)

DB-backed membership roles: Owner, Administrator, ServiceAdvisor.

Denied: Technician, Viewer. Database role changes take effect immediately.

### Read access

`OrganizationMember` for list/details. No anonymous or public estimate routes.

## Routes (internal)

| Method | Route |
|--------|-------|
| GET | `/estimates` |
| POST | `/repair-orders/{repairOrderId}/estimates` |
| GET | `/estimates/{estimateId}` |
| GET | `/estimates/{estimateId}/edit` |
| POST | `/estimates/{estimateId}/items` |
| POST | `/estimates/{estimateId}/items/{itemId}/edit` |
| POST | `/estimates/{estimateId}/items/{itemId}/remove` |
| POST | `/estimates/{estimateId}/present` |
| POST | `/estimates/{estimateId}/record-approval` |
| POST | `/estimates/{estimateId}/record-decline` |
| POST | `/estimates/{estimateId}/share` |
| POST | `/estimates/{estimateId}/share/rotate` |
| POST | `/estimates/{estimateId}/share/revoke` |

All mutations require anti-forgery tokens. No estimate delete route.

Customer portal routes (`/portal/*`) use a separate authentication scheme — see [customer-estimate-sharing-v1.md](customer-estimate-sharing-v1.md).

Draft estimates may add lines from the service/parts catalog (snapshot only) — see [catalog-and-inventory-v1.md](catalog-and-inventory-v1.md).

## Independence guarantees

Estimate workflow must not mutate:

- `RepairOrder.Status` / priority
- `RepairOrderTechnicianAssignment.WorkStatus`
- `Appointment.Status`
- `Inspection` / `InspectionItem` / `InspectionMediaAsset`

## Compliance note

STEP 15 is an internal quotation foundation only. It does not provide invoice, tax, fiscal receipt, or legally binding digital signature compliance.
