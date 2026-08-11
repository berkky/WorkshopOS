# WorkshopOS Rebrand Impact Matrix

**Purpose:** Assess what must change if the **market-facing brand** changes from the current working name **WorkshopOS**.  
**Status:** Planning matrix — no rebrand executed.

Columns: **Customer visible?** | **Must change?** | **Technical risk** | **Migration required?** | **Recommended timing**

---

## Customer-facing surfaces

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| Landing hero copy | "WorkshopOS unifies…" | **Yes** | **Yes** | Low | No | Phase 1 |
| Public navbar brand | `WorkshopOS` text | **Yes** | **Yes** | Low | No | Phase 1 |
| Public footer brand | `WorkshopOS` | **Yes** | **Yes** | Low | No | Phase 1 |
| Page `<title>` suffix | `… - WorkshopOS` | **Yes** | **Yes** | Low | No | Phase 1 |
| Login page | Title via layout | **Yes** | **Yes** | Low | No | Phase 1 |
| Onboarding complete | "Welcome to WorkshopOS" | **Yes** | **Yes** | Low | No | Phase 1 |
| App shell sidebar brand | `WorkshopOS` (×2) | **Yes** | **Yes** | Low | No | Phase 1 |
| Portal layout title | `… - WorkshopOS` | **Yes** | **Yes** | Low | No | Phase 1 |
| Portal footer | "Powered by WorkshopOS" | **Yes** | **Yes** (or remove line) | Low | No | Phase 1 |
| Portal access titles | `… - WorkshopOS` | **Yes** | **Yes** | Low | No | Phase 1 |
| Error page body | Generic (no brand name) | Partial | Optional | Low | No | Phase 1 if shell branded |
| README (buyer-facing) | WorkshopOS | **Yes** | **Yes** | Low | No | Phase 1–2 |
| Buyer FAQ / packaging docs | WorkshopOS | **Yes** | **Yes** | Low | No | Phase 2 |
| Release notes (forward) | WorkshopOS | **Yes** | **Yes** | Low | No | Phase 2 |

---

## Static assets

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| `hero-vehicle-wireframe.svg` | Path + artwork | Visible (image) | **No** (no text) | Low | No | Keep unless art direction changes |
| `hero-dashboard.svg` | Illustration | Visible | **No** (no text) | Low | No | Keep |
| `onboarding-success.svg` | Illustration | Visible | **No** (no text) | Low | No | Keep |
| Favicon / logo | Not present in RC | N/A | **Yes** when created | Low | No | Phase 1 |
| `images/workshopos/` URL path | Lowercase path | View-source only | Optional | Low | No | Phase 3 optional |

---

## CSS / JS identifiers

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| `wos-*` CSS classes | Internal selectors | No (class names) | **No** | High if mass-renamed | No | Keep internal |
| `workshopos-tokens.css` etc. | Filename in HTML | View-source only | Optional | Medium | No | Phase 3 optional |
| `workshopos-hero.js` | Filename in HTML | View-source only | Optional | Medium | No | Phase 3 optional |
| `WorkshopOS.Web.styles.css` | Scoped CSS bundle | View-source only | Optional | Medium | Tied to assembly name | Phase 3 |

**Assessment:** `wos-*` and `workshopos-*` file names are **internal implementation identifiers**, not market brand surfaces. No customer-visible brand leak from class prefixes alone.

---

## Documentation

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| Architecture docs | WorkshopOS references | Internal/dev | Optional | Low | No | Phase 2–3 |
| Setup/deployment docs | WorkshopOS + `workshopos_*` DB | Operator-facing | Product name **Yes**; DB names optional | Low | No | Phase 2 |
| STEP 01–24 historical RC reports | WorkshopOS | Internal audit | **No** (historical) | Low | No | Preserve history |
| Term sheet / legal pack | WorkshopOS product name | Buyer/legal | **Yes** | Low | No | After counsel + Phase 2 |
| THIRD-PARTY-NOTICES header | WorkshopOS label | Buyer | **Yes** (product label only) | Low | No | Phase 2 |

---

## Package / release

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| RC ZIP filename | `WorkshopOS-v1.0.0-rc1-source-step23.1.zip` | Distribution | **Yes** | Low | No | Next package build |
| SHA-256 manifest labels | WorkshopOS v1.0.0-rc1 | Distribution | **Yes** | Low | No | Next package build |
| Solution name | `WorkshopOS.slnx` | Developer | Optional | Medium | No | Phase 3 |
| NuGet/package identity | N/A (not published) | N/A | N/A | N/A | No | N/A |

---

## Projects / namespaces / assemblies

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| `WorkshopOS.Domain` | Namespace/assembly | No | **No** (default) | **Very high** | No | Phase 3 only if required |
| `WorkshopOS.Application` | Namespace/assembly | No | **No** | **Very high** | No | Phase 3 optional |
| `WorkshopOS.Infrastructure` | Namespace/assembly | No | **No** | **Very high** | No | Phase 3 optional |
| `WorkshopOS.Web` | Namespace/assembly | No | **No** | **Very high** | No | Phase 3 optional |
| Test project | `WorkshopOS.Infrastructure.IntegrationTests` | No | **No** | High | No | Phase 3 optional |
| ~1,201 namespace lines | `using WorkshopOS.*` | No | **No** | **Very high** | No | Avoid |

---

## Database

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| `workshopos_dev` | PostgreSQL database | No | **No** | Medium | **Yes** if renamed | Avoid |
| `workshopos_test` | Test database | No | **No** | Medium | **Yes** if renamed | Avoid |
| `workshopos_app` | DB role example | No | **No** | Medium | **Yes** if renamed | Avoid |
| `ConnectionStrings:WorkshopOS` | Config key | No | Optional | Low (secrets remap) | No | Phase 3 optional |

**Principle:** Brand identity ≠ database schema identity. Do not create EF migrations for branding.

---

## Auth / cookies / claims

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| Staff cookie | `WorkshopOS.Auth` | Browser devtools only | **No** (default) | **High** — logs users out | Session invalidation | Avoid cosmetic rename |
| Portal cookie | `WorkshopOS.CustomerPortal` | Devtools only | **No** | **High** | Portal session reset | Avoid |
| Portal auth scheme | `WorkshopOS.CustomerPortal` | Internal | **No** | High | Yes if changed | Phase 3 only |
| Claim types | `workshopos:estimate_share_public_id` | Internal token | **No** | Medium | Yes if changed | Phase 3 only |

---

## Configuration / secrets

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| Web UserSecretsId | `workshopos-web-foundation` | No | Optional | Medium — breaks local secrets | No | Phase 3 |
| Test UserSecretsId | `workshopos-infrastructure-integration-tests` | No | Optional | Medium | No | Phase 3 |
| `WORKSHOPOS_DESIGNTIME_CONNECTION` | Env var | No | Optional | Low | No | Phase 3 |

---

## Tests / migrations

| Surface | Current identifier | Customer visible? | Must change? | Technical risk | Migration required? | Recommended timing |
|---------|-------------------|-------------------|--------------|----------------|---------------------|-------------------|
| EF migrations (9) | `WorkshopOS.Infrastructure.Persistence.Migrations` | No | **DO NOT RENAME** | **Critical** | Historic identity | Never rewrite |
| Integration tests | Namespace references | No | Only if namespaces renamed | High | No | Avoid |
| ReleaseCandidateTests | Doc path assertions | No | Update if doc paths change | Low | No | As needed |

---

## Summary recommendation

| Priority | Action |
|----------|--------|
| **Must change for rebrand** | Customer-visible copy, titles, navbar/footer, portal powered-by, buyer/legal forward docs, package names |
| **Should not change by default** | Namespaces, projects, databases, migrations, cookies/auth schemes |
| **Optional later** | CSS/JS filenames, config keys, solution name |

See: [commercial-rebrand-playbook.md](commercial-rebrand-playbook.md)
