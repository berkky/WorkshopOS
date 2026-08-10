# WorkshopOS Commercial Entitlement Model

**Purpose:** Describe **contractual commercial scope** — not application RBAC.  
**Status:** Governance documentation for sales and legal review.  
**Enforcement:** Contractual only in v1.0.0-rc1 (no license-key DRM).

---

## Distinction: commercial entitlements vs. application roles

| Layer | What it controls | Examples |
|-------|------------------|----------|
| **Commercial entitlements** | What the customer organization is licensed to do/operate | Locations, source access, updates, white-label |
| **Application authorization** | What a signed-in staff user can do inside the product | Owner, manager, technician policies |

WorkshopOS membership roles (Owner, Manager, Technician, etc.) are **in-product authorization**. They do **not** replace a commercial license agreement.

---

## Commercial entitlement concepts

| Entitlement | Typical meaning | Standard Source Edition |
|-------------|-----------------|-------------------------|
| **Licensed organization** | Named legal entity permitted to use WorkshopOS | 1 entity per agreement |
| **Licensed workshop locations** | Count of operational sites covered | Base allowance + optional add-ons |
| **Source access** | Right to receive and use delivered source internally | Included |
| **Internal modification** | Right to change source for licensed internal use | Included within scope |
| **Internal deployment** | Right to run instances for licensed operations | Included within scope |
| **Backups** | Right to retain internal backup copies | Included (reasonable) |
| **Contractor access** | Third-party implementers under confidentiality | Permitted with obligations |
| **Updates** | Access to newer releases | Maintenance entitlement only |
| **Support** | Technical assistance | Purchased support tier |
| **White-label** | Rebrand/productize under customer brand | **Not included** |
| **Resale** | Deploy for unrelated customer organizations | **Not included** |
| **Sublicense** | Pass rights to third parties | **Not included** |
| **Public source publication** | Host proprietary source publicly | **Not included** |

---

## Location allowance model (preferred)

WorkshopOS is multi-location workshop software. Commercial scope should be expressed as:

```
Licensed Organization
  └── Licensed Location Allowance (e.g., 1, 3, unlimited-by-contract)
        └── Deployed WorkshopOS instance(s) serving those locations
```

Additional locations are a **commercial add-on**, not an automatic technical unlock.

---

## Version entitlement (preferred)

| Concept | Rule |
|---------|------|
| **Licensed version** | Customer has perpetual use rights to delivered version(s) named in agreement |
| **Future major releases** | Included only with active maintenance or upgrade purchase |
| **Support** | Applies to entitled versions per support agreement |

---

## Custom development entitlements

| Work type | Typical commercial treatment |
|-----------|------------------------------|
| Configuration / theming / setup | Customer-scoped services |
| Customer-specific modules | Contract/SOW-defined |
| Reusable platform features | Normally WorkshopOS product IP |

---

## What this document does not do

- Does not configure runtime license enforcement
- Does not replace signed order forms or license agreements
- Does not define in-app feature flags for commercial tiers (future product decision)

See also: [commercial-packaging.md](commercial-packaging.md), [commercial-buyer-faq.md](commercial-buyer-faq.md).
