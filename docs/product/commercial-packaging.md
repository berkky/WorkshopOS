# WorkshopOS Commercial Packaging Model

**Purpose:** Preferred commercial product architecture for sales and legal alignment.  
**Status:** No prices or SKUs defined in this document.

---

## Base product

### WorkshopOS Commercial Source Edition

| Attribute | Preferred definition |
|-----------|---------------------|
| Delivery | Source code + documentation for licensed release |
| License unit | Licensed legal entity + location allowance |
| Use rights | Internal operations within licensed scope |
| Modification | Internal customization permitted |
| Redistribution | Not permitted without separate written approval |
| Updates | Not perpetual by default |
| Support | Separate entitlement |

---

## Optional commercial modules (separate entitlements)

These are **commercially separate** from the base license unless explicitly bundled in an order form:

| Module | Description |
|--------|-------------|
| **Annual Maintenance** | Time-bounded access to product updates/upgrades |
| **Priority Support** | Elevated response/assistance tier (SLA defined in agreement) |
| **Additional Workshop Locations** | Expanded licensed location allowance |
| **Custom Development** | Scoped professional services / SOW work |
| **White-Label Rights** | Separate branding/trademark use permissions |
| **Partner / Reseller Rights** | Channel rights to deploy/sell to third parties |

---

## Preferred bundle logic

```
Commercial Source Edition
  ├── Perpetual use of licensed delivered version(s)
  ├── Source access + internal modification (licensed scope)
  └── Optional add-ons:
        ├── Maintenance (updates)
        ├── Support (assistance)
        ├── Extra locations
        ├── Custom development
        ├── White-label
        └── Partner/reseller (distinct agreement)
```

---

## What is intentionally not priced here

- Per-user / per-technician seat fees (not primary model)
- Public SaaS multi-tenant hosting by licensor (product supports self-host; hosting offer is commercial decision)
- Payment processing fees (product has no payment gateway)

---

## Relationship to technical product

| Technical capability | Commercial packaging note |
|---------------------|---------------------------|
| Multi-tenant organizations | Licensed customer operates their org(s) within scope |
| Multi-location operations | Location allowance is contractual |
| Customer portal | Included in product; not a separate SKU required |
| Inspection media / DVI | Included in product |
| Reporting | Included in product |

---

## Future editions (informational only)

A future **binary-only edition** might warrant different packaging (e.g., source escrow evaluation). v1.0.0-rc1 is positioned as **Commercial Source Edition**.

See: [commercial-entitlement-model.md](commercial-entitlement-model.md), [proprietary-commercial-license-term-sheet.md](../release/proprietary-commercial-license-term-sheet.md).
