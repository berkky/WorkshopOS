# Demo Runbook

Real-application demonstration workflow for WorkshopOS v1.0.0-rc1. **Create all data through the normal UI** in an isolated demo database — no startup seed.

## Prerequisites

- WorkshopOS running against a **demo** database (not production)
- Owner account created via `/onboarding`
- Second browser or incognito window for customer portal

## Narrative flow

### 1. Owner onboarding

- Open `/onboarding`
- Create workshop organization and owner credentials
- **Value:** self-service tenant provisioning

### 2. Customer CRM

- Navigate to **Customers** → Create
- Enter name, contact details
- **Value:** tenant-scoped customer records

### 3. Vehicle

- **Vehicles** → Create, link to customer
- **Value:** vehicle history tied to customer

### 4. Appointment

- **Appointments** → Create for customer/vehicle
- **Value:** scheduling foundation

### 5. Repair order

- Create repair order from appointment or walk-in
- **Value:** operational job record

### 6. Technician assignment

- **Operations** → assign technician to repair order
- **Value:** workshop floor coordination

### 7. Digital vehicle inspection (DVI)

- **Inspections** → create and start inspection
- Complete checklist items
- Upload photo evidence (JPEG/PNG/WebP)
- Complete inspection
- **Value:** structured DVI with private evidence

### 8. Estimate

- **Estimates** → create from repair order
- Add catalog service/part lines
- **Present** estimate to customer
- **Value:** commercial quote with immutable snapshots

### 9. Secure customer portal

- Create estimate share link
- Copy link (`/portal/access/{id}#token=...`)
- Open in customer browser (incognito)
- Customer approves estimate
- **Value:** secure fragment-based share; no customer account required

### 10. Invoice and payment

- **Invoices** → create from approved estimate
- **Issue** invoice
- Record full **payment** (ledger metadata)
- **Value:** workshop-entered billing ledger

### 11. Complete and commercial close

- Complete repair order
- **Commercial close** on repair order
- **Value:** end-to-end job closure

### 12. Dashboard and reports

- **Dashboard** → operational overview
- **Reports** → operations and commercial KPIs (read-only)
- **Value:** management visibility without write risk

### 13. Catalog and inventory (overview)

- **Catalog** → services and parts
- **Inventory** → view balances; optional adjustment demo
- **Value:** catalog-backed estimates; immutable movement ledger

## Alternate path: staff-recorded approval

Skip portal approval; use **Record approval** on a presented estimate. Demonstrates phone/in-person customer decisions.

## Security talking points

- Tenant isolation on every module
- Private inspection media (staff-only)
- Portal auth separate from staff auth
- Share token in URL fragment, hash cleared before POST

## Demo data policy

- Use isolated demo database/environment
- Do not use production data
- No automatic demo seed at startup
