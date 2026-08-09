# WorkshopOS Commercial Boundaries v1

## Three conceptual boundaries

### 1. Platform / SaaS boundary

Owned by WorkshopOS as the product operator. **Not modeled as domain entities in v1.**

Planned capabilities:

- Subscription plans
- Feature entitlements
- Platform billing
- Organization provisioning
- Platform administrator access

**Rule:** `Subscription` is never a parent of operational entities such as `Customer`, `Vehicle`, or `RepairOrder`.

Correct tenant root for workshop operations:

```text
Organization
```

### 2. Workshop operational boundary

Owned by the automotive service business (the organization). **Modeled in v1 domain.**

Includes:

- Workshop locations
- Customers and vehicles
- Appointments and repair orders
- Inspections and estimates

This is the core revenue-generating workflow surface for WorkshopOS customers.

### 3. Customer-facing portal boundary

End-customer experience for approvals, status, and communication. **Planned, not implemented in v1.**

Planned capabilities:

- Customer portal access
- Public approval links for estimates
- Shareable inspection summaries
- Secure tokens separate from database primary keys

Public identifiers will not expose raw `Guid` primary keys from domain entities.

## Planned commercial capabilities (not yet implemented)

| Capability | Status |
|---|---|
| Multi-location per organization | Domain-ready (`WorkshopLocation`) |
| Subscription plans | Planned (platform boundary) |
| Feature entitlements | Planned (platform boundary) |
| Customer portal | Planned |
| Public approval links | Planned |
| AI assistance | Planned (application service, not core domain) |
| Payments and invoicing | Planned (post-estimate workflow) |
| Inventory / parts | Planned (post core repair flow) |

## Workflow focus for near-term product

v1 domain prioritizes the path:

```text
Appointment (optional)
    → RepairOrder
        → Inspection
        → Estimate
        → Customer approval (future)
        → Invoice / payment (future)
```

Inventory accounting and invoice entities are intentionally deferred to avoid premature financial modeling.

## AI boundary

AI features (recommendations, diagnostics assistance, chat) will be introduced as **application services** targeting specific use cases. They will not become core domain entities or vendor-coupled domain models.

## Authentication vs operational identity

| Concern | v1 status |
|---|---|
| `Customer` entity | Operational record only |
| Customer login / portal identity | Planned separately |
| Staff / technician identity | Planned separately |
| Organization membership | Planned separately |

Authentication models must not be prematurely merged with workshop operational entities.
