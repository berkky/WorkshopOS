# Catalog & Inventory v1 (STEP 17)

STEP 17 introduces reusable service and parts catalogs, location-scoped physical inventory, and estimate catalog snapshot helpers.

## Scope

**Service catalog**
- Reusable `ServiceCatalogItem` entries (code, name, description, default selling price)
- Active/inactive lifecycle, tenant-scoped code uniqueness
- Search and pagination

**Parts catalog**
- Reusable `PartCatalogItem` entries (SKU, name, description, default selling price)
- Active/inactive lifecycle, tenant-scoped SKU uniqueness
- Cannot deactivate while any location has `QuantityOnHand > 0`

**Inventory**
- `PartInventoryBalance` — one balance per Part + WorkshopLocation
- `PartInventoryMovement` — immutable adjustment ledger with `BalanceAfter`
- Manual stock adjustments only (OpeningBalance, ManualIncrease, ManualDecrease)
- No negative on-hand inventory
- Serializable transaction boundary for adjustments

**Estimate integration**
- Add active service/part from catalog to **Draft** estimate as financial snapshot
- No catalog FK on `EstimateItem` — snapshot only
- Catalog price/name changes do **not** rewrite historical estimate lines
- Adding parts to estimates does **not** reserve or consume stock

**Not in STEP 17:** suppliers, purchase orders, goods receiving, barcode scanning, stock transfers, reservations, repair-order consumption, automatic decrement, reorder automation, vendor pricing, invoice/payment, tax, valuation/FIFO/LIFO, customer portal inventory access.

## Catalog vs estimate snapshot

| | Catalog item | EstimateItem |
|---|-------------|--------------|
| Price | Mutable current suggestion | Immutable financial snapshot |
| Currency | Set at creation from organization | Set at estimate creation |
| Lifecycle | Active/inactive | Draft edit / Sent immutable |

When a catalog item is added to a draft estimate, the service copies current name/description representation, quantity, and `DefaultUnitPrice` into `EstimateItem`. Later catalog edits do not affect existing lines.

## ServiceCatalogItem

Tenant-owned. Fields: `Code` (normalized uppercase), `Name`, `Description?`, `DefaultUnitPrice`, `CurrencyCode`, `IsActive`, timestamps.

- `CurrencyCode` from `Organization.DefaultCurrencyCode` at creation (server-controlled)
- Unique: `(OrganizationId, Code)`
- No hard delete — deactivate preserves row

## PartCatalogItem

Same pattern with `Sku` instead of `Code`. Unique: `(OrganizationId, Sku)`.

Deactivation blocked while any `PartInventoryBalance.QuantityOnHand > 0` for the part.

## PartInventoryBalance

- One row per `(OrganizationId, PartCatalogItemId, WorkshopLocationId)`
- `QuantityOnHand` — `numeric(12,3)`, check constraint `>= 0`
- Lazy creation on first adjustment (no row = 0 on hand)
- Composite FKs to `PartCatalogItem` and `WorkshopLocation`

## PartInventoryMovement

Immutable ledger row per adjustment:

- `MovementType`: OpeningBalance, ManualIncrease, ManualDecrease
- Positive quantity in command; service computes signed `QuantityDelta`
- `BalanceAfter` authoritative after adjustment
- `RecordedByUserId` server-derived, FK to `OrganizationMembership`
- Composite FK to balance alternate key `(OrganizationId, PartCatalogItemId, WorkshopLocationId)`
- No update/delete — corrections via new compensating adjustment

## Authorization

### CatalogManager (DB membership)

Allowed: Owner, Administrator, ServiceAdvisor  
Denied: Technician, Viewer

Service advisors maintain quoting catalog but cannot adjust physical stock.

### InventoryManager (DB membership)

Allowed: Owner, Administrator  
Denied: ServiceAdvisor, Technician, Viewer

### Read access

`OrganizationMember` for catalog and inventory list/history.

## Estimate catalog operations

`AddServiceCatalogItemAsync` / `AddPartCatalogItemAsync`:

- Estimate must be `Draft`
- Catalog item active, same tenant
- `catalog.CurrencyCode == estimate.CurrencyCode`
- Creates `EstimateItem` with type `Service` or `Part`

Does **not** create inventory movements or change balances.

## Customer portal isolation

`WorkshopOS.CustomerPortal` does not authorize `/catalog/*` or `/inventory/*`. Portal estimate projection remains snapshot-only — no stock quantities or catalog management data.

## Concurrency

Inventory adjustments use PostgreSQL `Serializable` transactions. `40001` and first-balance `23505` races map to safe `ConcurrencyConflict`.

## Independence guarantees

The following must **not** change inventory:

- Adding catalog part to draft estimate
- Estimate present / approve / decline (staff or portal)
- Repair order create
- Technician work start
- Invoice issue / payment / commercial close
- Management reporting reads (STEP 19) — no inventory valuation metrics
