# Sales Demo Script

10–15 minute WorkshopOS product demonstration. Adjust pacing to audience.

## Opening (1 min)

**Say:** WorkshopOS is premium automotive workshop management — from appointment to paid job, with secure customer estimate approval and private inspection evidence.

**Show:** Public landing `/` — premium hero, product positioning.

**Security note:** Dual auth schemes (staff vs customer portal).

---

## Onboarding (1 min)

**Click:** `/onboarding` → create workshop + owner.

**Explain:** Multi-tenant SaaS; each workshop is an isolated organization.

**Value:** Fast time-to-value without separate provisioning.

---

## Customer and vehicle (1 min)

**Click:** Customers → Create → Vehicles → Create.

**Explain:** CRM + vehicle history in one tenant.

**Value:** Single source of truth for service history.

---

## Appointment and repair order (2 min)

**Click:** Appointments → Create → Repair Orders → Create/convert.

**Explain:** Structured intake from booking to shop floor.

**Value:** Reduces lost jobs between front desk and technicians.

---

## Operations and DVI (3 min)

**Click:** Operations → assign technician → Inspections → start → complete items → upload photos → complete.

**Explain:** Digital Vehicle Inspection with private photo evidence.

**Security:** Media outside web root; staff-authenticated delivery only.

**Value:** Documented condition before estimate — reduces disputes.

---

## Estimate and portal approval (3 min)

**Click:** Estimates → add lines → Present → Create share → open link in incognito.

**Explain:** Customer approves via secure link — no account password.

**Security:** Token in URL fragment; hash cleared; server hash-only storage.

**Value:** Faster approvals; audit trail of customer decision.

---

## Invoice, payment, close (2 min)

**Click:** Invoice from estimate → Issue → Record payment → Complete RO → Commercial close.

**Explain:** Ledger-style payments (not a payment gateway).

**Value:** Clear commercial closure; reporting-ready data.

---

## Reports and catalog (2 min)

**Click:** Dashboard → Reports → Catalog/Inventory overview.

**Explain:** Read-only executive KPIs; inventory movement ledger.

**Value:** Management visibility without operational risk.

---

## Closing (1 min)

**Summarize:** End-to-end workshop workflow with security-by-design.

**Do not claim:** Payment gateway integration, tax compliance, email automation, AI diagnostics, or formal security certifications.

**Next step:** Isolated pilot environment with customer’s own demo data.
