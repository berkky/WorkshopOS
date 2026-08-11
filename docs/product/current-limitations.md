# Current Limitations

Commercially honest scope boundaries for WorkshopOS v1.0.0-rc1.

## Payments and billing

- Payment records are **workshop-entered ledger metadata** only.
- No payment gateway, card capture, or bank verification.
- Not PCI DSS scope — do not collect PAN/CVV.

## Tax and compliance

- No tax/VAT calculation engine.
- No e-invoice or legal digital signature support.
- No GDPR/KVKK compliance tooling — deployer responsible for data protection obligations.

## Communications

- No automated email or SMS for estimate share delivery.
- Staff must copy/share portal links through their own channels.

## Customer access

- No general customer login account.
- Customer portal is **estimate-share scoped** only.
- One share session cannot access other estimates or staff modules.

## Inspection media

- DVI photos are **staff-only**; not shared with customers via portal.
- EXIF metadata stripping: **not implemented**.
- Malware scanning on uploads: **not implemented**.

## Inventory

- Movement ledger only — no FIFO/LIFO valuation.
- No supplier purchase-order workflow.
- Inventory is not mutated by estimates, invoices, or portal approvals.

## Security and operations

- Global CSP with nonces/hashes: **deferred**.
- Live HTTP antiforgery-failure test: not in test harness (source audit confirms 100% POST coverage).
- WorkshopOS does not perform automatic backups.

## Platform

- Native **Windows runtime not executed** in verification cycle (cross-publish verified; harness ready — see [windows-native-validation.md](../release/windows-native-validation.md)).
- `win-x64` framework-dependent and self-contained cross-publish **verified** from macOS (STEP 26/28).
- Health probes: `/health/live` (liveness) and `/health/ready` (PostgreSQL readiness) — STEP 27.
- Repair orders index uses **server-side pagination** (default 20, max 100) — STEP 27.
- DM Sans typography is **self-hosted locally** under SIL OFL 1.1 (no runtime Google Fonts dependency).

## Visual QA

- Independent browser screenshots at 375/768/1280/1440: **not captured** (STEP 29 used 1440×1000 desktop and 390×844 mobile authenticated matrix).
- Authenticated staff UI visual acceptance: **PASS (STEP 29)** — real-browser walkthrough on `workshopos_test` with disposable synthetic data and post-QA cleanup; see [manual-visual-qa.md](../release/manual-visual-qa.md).

## Release engineering blockers (STEP 28)

The following remain **true blockers** before commercial distribution:

1. **Native Windows execution evidence** — harness ready; requires trusted Windows runner
2. **Final commercial brand clearance** — WorkshopOS is internal working name only
3. **Final proprietary LICENSE / legal terms** — pending counsel review
4. **Monitored security contact** — see `SECURITY.md` publication readiness
5. **Canonical release commit/tag** — not authorized

## Distribution

- **Proprietary Commercial Source License** direction chosen by owner.
- **Final legal LICENSE / agreement pending** counsel and owner approval — not COMMERCIAL DISTRIBUTION READY.
- See [commercial-buyer-faq.md](commercial-buyer-faq.md) and [../release/proprietary-commercial-license-term-sheet.md](../release/proprietary-commercial-license-term-sheet.md).

These are explicit product scope boundaries unless listed as defects in release notes.
