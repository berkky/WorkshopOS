# WorkshopOS Release Governance

**Purpose:** Define the technical and commercial release process for WorkshopOS.  
**Status:** Governance documentation — no git mutations performed by this document.

---

## Release principles

1. Technical verification and legal/commercial closure are **separate gates**.
2. No release commit, tag, or push without **explicit owner authorization**.
3. Source packages must build with **0 errors / 0 warnings**.
4. Proprietary licensing is **contractual** in v1.0.0-rc1 — no DRM enforcement in product.

---

## Release process (canonical sequence)

| Step | Activity | Owner / role |
|------|----------|--------------|
| 1 | **Working-tree closure** — intended source inventory complete, artifacts excluded | Engineering |
| 2 | **Test/build/security gates** — full suite, warnaserror, vulnerability audit | Engineering |
| 3 | **Dependency audit** — NuGet vulnerable package scan | Engineering |
| 4 | **Model drift check** — `has-pending-model-changes` = NO | Engineering |
| 5 | **Package creation** — clean source snapshot → ZIP | Engineering |
| 6 | **Source/package secret audit** — no credentials, keys, runtime media | Engineering |
| 7 | **License/legal review** — final agreement ready | Owner / counsel |
| 8 | **SECURITY contact** — monitored reporting address configured | Owner |
| 9 | **Owner commit approval** — explicit authorization to commit | Owner |
| 10 | **Canonical release commit** — represents release source in VCS | Engineering (after approval) |
| 11 | **Annotated release tag** — e.g. `v1.0.0` | Engineering (after approval) |
| 12 | **Package hash** — SHA-256 recorded for distribution artifact | Engineering |
| 13 | **SBOM** — generated per [sbom-strategy.md](sbom-strategy.md) | Engineering |
| 14 | **Owner push approval** — explicit authorization to push | Owner |
| 15 | **Remote release publication** — GitHub/release portal/customer delivery | Owner |
| 16 | **Post-release verification** — smoke test on published artifact | Engineering |

---

## v1.0.0-rc1 current status

| Gate | Status |
|------|--------|
| Working-tree technical closure | Complete (STEPS 01–23.2) |
| Tests / builds | 657 pass; 0 warnings |
| Migrations / model drift | 9 applied; 0 pending; no drift |
| Dev DB evidence | `workshopos_dev` verified (read-only) |
| RC source ZIP | `WorkshopOS-v1.0.0-rc1-source-step23.1.zip` |
| Proprietary term sheet | Drafted (STEP 24) |
| Final legal LICENSE | **Pending** |
| Security contact | **Pending** |
| Release commit/tag | **Not authorized** |

---

## What this process does not include

- Automatic migration at application startup
- License-key activation or phone-home enforcement
- Production deployment (separate [deployment runbook](../deployment/deployment-runbook.md))

---

## Related documents

- [release-checklist.md](release-checklist.md)
- [source-package-manifest.md](source-package-manifest.md)
- [legal-review-pack.md](legal-review-pack.md)
- [proprietary-commercial-license-term-sheet.md](proprietary-commercial-license-term-sheet.md)
