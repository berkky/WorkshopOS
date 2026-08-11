# WorkshopOS Commercial Rebrand Playbook

**Purpose:** Safe, phased approach if the owner selects a **different market-facing brand** than the current working name **WorkshopOS**.  
**Status:** Planning document — **do not execute rebrand in STEP 24.1**.

---

## Core principle

Separate **market-facing brand** from **technical internal identifiers**.

A commercial rebrand does **not** automatically require renaming every namespace, database, migration, or project identifier.

Default recommendation: **minimize technical churn** unless a customer-visible leak or legal requirement demands it.

---

## Phase 1 — Market-facing name, copy, and assets (HIGH PRIORITY)

**Goal:** Customer/buyer never sees the old brand where it creates confusion.

| Surface | Action |
|---------|--------|
| Landing page hero/marketing copy | Replace visible product name |
| Navbar / footer brand text | Replace wordmark text |
| `<title>` tags and meta descriptions | Update suffix brand |
| Login / onboarding copy | Update welcome and CTA text |
| App shell sidebar/topbar brand label | Update visible label |
| Customer portal footer ("Powered by …") | Update if retaining powered-by line |
| Portal access page titles | Update |
| Error page shell (if branded) | Update surrounding layout brand |
| README buyer-facing sections | Update for new market brand |
| Buyer FAQ, packaging, term sheet product name | Update **future-facing** docs |
| Release notes (forward-looking) | Update product name for next release |

**Assets likely requiring regeneration:**

| Asset | Reason |
|-------|--------|
| Logo/wordmark (if text embedded) | Brand text change |
| SVG assets with embedded product name | Audit — current SVGs appear text-free |
| Favicon (when added) | New brand mark |
| Social/OG images (when created) | Marketing brand |

**Assets that may remain unchanged in Phase 1:**

| Asset | Reason |
|-------|--------|
| `hero-vehicle-wireframe.svg` | No product name text detected |
| `hero-dashboard.svg` | Illustration only |
| `onboarding-success.svg` | Illustration only |
| `wos-*` CSS classes | Internal implementation |
| `workshopos-*.css` filenames | Internal unless view-source leakage is a concern |

---

## Phase 2 — Package, release, and commercial documentation

**Goal:** Align distribution artifacts and legal drafts with final market brand.

| Item | Action |
|------|--------|
| RC / source ZIP filename | e.g. `<NewBrand>-v1.0.0-source.zip` |
| Release notes heading | Update product name |
| Source package manifest | Update product label |
| Proprietary term sheet product name | Update with counsel |
| Legal review pack | Update references |
| SBOM product name metadata | Update at release generation |
| `THIRD-PARTY-NOTICES.md` header | Update product label (not third-party licenses) |

**Historical records:** STEP 01–24 technical reports and RC ZIP hashes referencing WorkshopOS may remain as **historical technical evidence**. Do not carelessly rewrite audit history; add forward-looking documents for the new brand.

---

## Phase 3 — Optional technical identifier cleanup (LOW PRIORITY / HIGH RISK)

Only if required by legal counsel, acquisition integration, or deliberate platform rename.

| Identifier | Default recommendation |
|------------|------------------------|
| `WorkshopOS.*` namespaces | **CAN SAFELY REMAIN INTERNAL** |
| Project/assembly names (`WorkshopOS.Web`, etc.) | **OPTIONAL FUTURE CLEANUP** — high churn |
| Solution file name | Optional |
| `workshopos_dev` / `workshopos_test` DB names | **CAN REMAIN INTERNAL** — no customer visibility |
| `workshopos_app` DB role | Internal |
| `ConnectionStrings:WorkshopOS` | Internal config key — rename only if desired |
| `UserSecretsId` (`workshopos-web-foundation`) | Internal — rename breaks local secrets mapping |
| EF migration namespaces/history | **DO NOT RENAME** historic migrations |
| Cookie names (`WorkshopOS.Auth`) | **Avoid cosmetic rename** — invalidates sessions |
| Auth scheme (`WorkshopOS.CustomerPortal`) | Internal — rename requires auth migration plan |
| Claim types (`workshopos:…`) | Internal — rename requires portal session migration |
| `WORKSHOPOS_DESIGNTIME_CONNECTION` | Internal env var |
| `wos-*` CSS prefix | Internal — no market brand leak |
| `workshopos-hero.js` filename | Low priority; visible in HTML source |

---

## Rebrand execution order (recommended)

```
1. Brand clearance complete (see brand-clearance-checklist.md)
2. Final market name selected by owner
3. Phase 1 UI/copy/assets
4. Counsel updates LICENSE / term sheet product name
5. Phase 2 package/release docs
6. New RC ZIP with updated naming
7. Phase 3 technical cleanup ONLY if justified
8. Release commit/tag (STEP 25+)
```

---

## Premium visual preservation

A market brand change must **not** destroy the accepted visual system:

- Dark luxury-tech palette (graphite/navy, cobalt/cyan)
- Layered glass surfaces and depth
- Automotive wireframe motif
- Restrained 3D hero motion on public pages
- Premium SaaS quality across authenticated surfaces

Rebrand changes **name and marks**, not the design north star.

---

## Testing after Phase 1

Minimum regression after customer-facing copy changes:

- `dotnet build` Debug/Release/warnaserror — 0 warnings
- `dotnet test` — full suite pass
- Public routes visual smoke: `/`, `/account/login`, `/onboarding`
- Portal access pages render correctly

No schema/migration changes expected for name-only rebrand.

---

## Related documents

- [brand-clearance-checklist.md](brand-clearance-checklist.md)
- [rebrand-impact-matrix.md](rebrand-impact-matrix.md)
- [brand-governance.md](../product/brand-governance.md)
