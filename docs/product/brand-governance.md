# WorkshopOS Brand Governance

**Purpose:** Record brand, trademark, visual identity, and **commercial clearance status**.  
**Status:** Working name in use; **commercial brand not cleared**.

---

## Product naming status (STEP 24.1 – 24.5)

| Item | Status |
|------|--------|
| **Working / internal name** | **WorkshopOS** (current repository product name — unchanged) |
| **Commercial / market brand** | **NOT SELECTED** |
| **Round I (STEP 24.2)** | **REJECTED** after preliminary external commercial screening |
| **Round II (STEP 24.3–24.4)** | **REJECTED** after independent external commercial screening |
| **Round III (STEP 24.4–24.5)** | **REJECTED** after independent preliminary external commercial screening |
| **Automated Cursor naming** | **FROZEN** — no further AI naming rounds in repository |
| **Next candidate set** | **CLEARANCE-FIRST PROCESS REQUIRED** — see [final-commercial-brand-brief.md](../release/final-commercial-brand-brief.md) |
| **Rejected-name register** | [rejected-commercial-brand-register.md](../release/rejected-commercial-brand-register.md) |
| **Counsel handoff** | [brand-counsel-handoff.md](../release/brand-counsel-handoff.md) |
| **Legally cleared for commercial use** | **NO** |
| **Trademark registration claimed** | **NO** |
| **Final market brand decision** | **OWNER / TRADEMARK COUNSEL PENDING** |

**Important:** Treat **WorkshopOS** as the **internal working name only** until a new candidate passes the clearance-first pipeline, receives counsel review, and the owner authorizes rebrand.

External finding (STEP 24.1): independent market research identified active automotive/workshop software products already using the exact name **WorkshopOS**. Rounds I–III produced **no cleared finalist**. All rejected names are recorded in the master rejected register. **No candidate is legally cleared** by this repository.

See: [final-commercial-brand-brief.md](../release/final-commercial-brand-brief.md) · [brand-counsel-handoff.md](../release/brand-counsel-handoff.md) · [rejected-commercial-brand-register.md](../release/rejected-commercial-brand-register.md) · [brand-clearance-checklist.md](../release/brand-clearance-checklist.md)

---

## Product identity (working name)

| Item | Value |
|------|-------|
| **Working product name** | WorkshopOS |
| **Intended commercial model** | Proprietary Commercial Source Edition |
| **Category** | Premium automotive workshop operations software (source + self-hosted deployment) |
| **Release candidate** | v1.0.0-rc1 |

---

## Market brand vs. technical identity

| Layer | Current state | Rebrand default |
|-------|---------------|-----------------|
| **Market-facing brand** | WorkshopOS (uncleared) | Change if counsel directs |
| **Namespaces / assemblies** | `WorkshopOS.*` | Keep internal unless required |
| **Database names** | `workshopos_dev`, `workshopos_test` | Keep internal |
| **CSS prefix** | `wos-*` | Keep internal |
| **Design system files** | `workshopos-*.css` | Optional cleanup later |

See: [commercial-rebrand-playbook.md](../release/commercial-rebrand-playbook.md), [rebrand-impact-matrix.md](../release/rebrand-impact-matrix.md)

---

## Visual north star (permanent product standard)

A market brand change must **not** destroy the accepted visual system. Customer-facing and authenticated UI must preserve **premium luxury-tech** identity:

| Element | Standard |
|---------|----------|
| Palette | Dark graphite/navy base, cobalt/cyan accents |
| Surfaces | Layered glass panels, depth, restrained glow |
| Brand motif | Automotive wireframe / operational intelligence aesthetic |
| Motion | Premium, restrained; quieter on mobile; respects `prefers-reduced-motion` |
| Quality bar | Executive SaaS feel — **not** default Bootstrap/scaffold appearance |

New UI work must not regress to unstyled tables, raw `bg-light` cards, or generic scaffold pages without premium design overrides.

Operational screens should remain efficient — excessive animation is not required on data-heavy workflows.

See: [design-system-and-ux-v1.md](../architecture/design-system-and-ux-v1.md)

---

## Trademark

| Item | Status |
|------|--------|
| WorkshopOS trademark registration | **NOT CLAIMED** in this repository |
| Legal clearance | **NOT COMPLETE** |
| Logo ownership | WorkshopOS licensor / owner (legal entity TBD) |
| Customer trademark rights | **Not granted** in standard license; white-label requires separate agreement |

Do not state that WorkshopOS is a registered trademark unless documented evidence exists.

---

## Domain and public presence

| Item | Status |
|------|--------|
| Official commercial domain | **OWNER DECISION PENDING** |
| Security contact | **NOT CONFIGURED** — do not invent domain/email |
| Preferred future pattern | `security@<official-domain>` after domain selected |
| Public marketing site | Owner decision |

**Do not configure `security@<domain>` until final commercial domain and brand are selected.**

---

## Brand asset inventory (original — working name era)

| Asset | Rebrand impact |
|-------|----------------|
| `hero-dashboard.svg` | Illustration — likely reusable |
| `hero-vehicle-wireframe.svg` | Illustration — likely reusable |
| `onboarding-success.svg` | Illustration — likely reusable |
| `workshopos-*.css` | Internal filenames — optional rename |
| `workshopos-hero.js` | Internal filename — optional rename |
| Text wordmark in UI | **Must change** if market brand changes |

Third-party brand assets (Bootstrap, jQuery, DM Sans/OFL) remain under their respective licenses.

---

## White-label policy (preferred)

White-label, co-branding, and trademark modification are **not** included in the standard Commercial Source Edition. Separate written agreement required.

---

## Related documents

- [brand-clearance-checklist.md](../release/brand-clearance-checklist.md)
- [final-commercial-brand-brief.md](../release/final-commercial-brand-brief.md)
- [rejected-commercial-brand-register.md](../release/rejected-commercial-brand-register.md)
- [brand-counsel-handoff.md](../release/brand-counsel-handoff.md)
- [commercial-brand-candidate-template.md](../release/commercial-brand-candidate-template.md)
- [commercial-brand-naming-sprint.md](../release/commercial-brand-naming-sprint.md) — Round I history
- [commercial-brand-naming-round-2.md](../release/commercial-brand-naming-round-2.md) — Round II history
- [commercial-brand-naming-round-3.md](../release/commercial-brand-naming-round-3.md) — Round III history
- [commercial-rebrand-playbook.md](../release/commercial-rebrand-playbook.md)
- [rebrand-impact-matrix.md](../release/rebrand-impact-matrix.md)
- [commercial-packaging.md](commercial-packaging.md)
- [proprietary-commercial-license-term-sheet.md](../release/proprietary-commercial-license-term-sheet.md)
- [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)
