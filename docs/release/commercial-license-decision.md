# Commercial License Decision

WorkshopOS v1.0.0-rc1 — **project license status**

## Owner decision (STEP 24)

The owner has selected:

**Proprietary Commercial Source License**

as the intended commercial model for WorkshopOS Commercial Source Edition.

This is a **business direction**, not a finalized legal instrument. A signed agreement and/or repository `LICENSE` file still require owner and qualified legal review.

## Current state

| Item | Status |
|------|--------|
| Commercial direction | **Proprietary Commercial Source License** |
| Term sheet (draft) | [proprietary-commercial-license-term-sheet.md](proprietary-commercial-license-term-sheet.md) |
| Decision matrix | [license-decision-matrix.md](license-decision-matrix.md) |
| Final `LICENSE` / agreement | **Pending** |
| Open-source license (MIT/Apache/GPL) | **Not selected** for WorkshopOS itself |

WorkshopOS source code **does not yet include a project-level LICENSE file**. Third-party components are documented in [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md). That documentation does **not** grant rights to WorkshopOS itself.

## Why this matters

Even with an owner direction chosen, distribution without a finalized agreement creates ambiguity about:

- Exact permitted and restricted uses
- Deployment and location scope
- Modification and derivative-work rights
- Redistribution and resale boundaries
- Warranty and liability limits
- Governing law and venue

Technical release-candidate verification (build, test, security baseline) is **separate** from legal distribution readiness.

## Preferred commercial model (summary)

| Topic | Preferred position |
|-------|-------------------|
| Product | WorkshopOS Commercial Source Edition |
| Scope unit | Licensed legal entity + location allowance |
| Source delivery | Yes, under confidentiality and restrictions |
| Internal modification | Permitted within licensed scope |
| Redistribution / resale | Not permitted without separate written approval |
| White-label / reseller | Separate commercial agreement |
| Perpetual use | Licensed delivered version(s) |
| Updates | Separate maintenance entitlement |
| DRM | None in v1.0.0-rc1 |

Full detail: [proprietary-commercial-license-term-sheet.md](proprietary-commercial-license-term-sheet.md)

## Third-party components

WorkshopOS includes third-party software under **their own licenses** (MIT, Apache-2.0, PostgreSQL License, SIL OFL for DM Sans, etc.). WorkshopOS proprietary terms must be **compatible** with those obligations. See [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).

## Security reporting contact

[SECURITY.md](../../SECURITY.md) requires a **real, monitored security contact** before public distribution. Preferred future pattern: `security@<official-workshopos-domain>` — domain not yet finalized.

## Recommended next steps

1. Counsel review of [legal-review-pack.md](legal-review-pack.md).
2. Finalize license agreement and publish `LICENSE` or customer contract template.
3. Configure security contact in `SECURITY.md`.
4. Proceed with **release commit/tag** only after explicit owner authorization (STEP 25).

## Disclaimer

This document is **not legal advice**. Consult qualified legal professionals for licensing, export, privacy, and compliance questions.
