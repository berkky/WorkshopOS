# Inspection Media v1

STEP 14 adds secure **photo evidence** for Digital Vehicle Inspection (DVI) checklist items.

## Scope

- Attach photos to specific `InspectionItem` rows
- Private filesystem storage (not PostgreSQL, not `wwwroot`)
- Tenant-owned `InspectionMediaAsset` metadata
- Authenticated delivery via `/inspection-media/{id}/content`
- Manager or assigned-technician upload/remove while inspection is **InProgress**
- Completed inspection media is **immutable** (read-only)
- Soft removal preserves audit metadata

**Not in STEP 14:** video, audio, PDF, SVG, GIF, HEIC, cloud storage, public URLs, share tokens, customer portal, thumbnails, image transformation, EXIF stripping, antivirus scanning.

## InspectionMediaAsset

Tenant-owned metadata entity:

- Links to `Inspection` and `InspectionItem` (both required)
- Opaque `StorageKey` (server-generated, path-safe, globally unique)
- `MediaKind.Photo` only
- Server-detected `ContentType`, canonical extension
- `LengthBytes`, lowercase hex `Sha256` (64 chars)
- Optional `Caption` (max 500, plain text)
- `UploadedByUserId` / `UploadedAtUtc` (server-controlled)
- `RemovedAtUtc` / `RemovedByUserId` for soft removal

Database enforces:

- `(OrganizationId, InspectionId)` → `Inspection`
- `(OrganizationId, InspectionId, InspectionItemId)` → `InspectionItem` alternate key
- `(OrganizationId, UploadedByUserId)` → `OrganizationMembership`

No cascade delete.

## Private storage

Default root: `<ContentRoot>/App_Data/inspection-media` (configurable via `InspectionMedia:StorageRootPath`).

- Never under `wwwroot`
- Never exposed via `UseStaticFiles`
- `FileMode.CreateNew` — no silent overwrite
- Path traversal rejected; client filenames never become paths

## Supported formats

| Format | Content-Type | Validation |
|--------|--------------|------------|
| JPEG | `image/jpeg` | Magic bytes `FF D8 FF` |
| PNG | `image/png` | PNG signature |
| WebP | `image/webp` | `RIFF....WEBP` |

SVG is explicitly rejected. Declared MIME/extension must match detected format.

## Limits

| Limit | Value |
|-------|-------|
| Max photo size | 8 MiB |
| Max HTTP multipart | 9 MiB |
| Max photos per item | 6 active |
| Max photos per inspection | 40 active |

Removed photos do not count toward limits.

## Authorization

**Upload / remove (InProgress only):**

- InspectionManager (Owner, Administrator, ServiceAdvisor), or
- Assigned technician (active membership + linked active Technician `StaffMember` + active `RepairOrderTechnicianAssignment`)

**View:** any `OrganizationMember` in current tenant (no anonymous access).

## Lifecycle

| Inspection status | Upload | Remove | View |
|-------------------|--------|--------|------|
| Draft | Denied | Denied | Allowed |
| InProgress | Allowed (authorized) | Allowed (authorized) | Allowed |
| Completed | Denied | Denied | Allowed |

Photo evidence does **not** change `InspectionCondition` or `RepairOrder` status.

## File / database consistency

Filesystem and PostgreSQL are not atomically transactional.

Upload workflow:

1. Validate and detect format
2. Write private file with opaque key (`CreateNew`)
3. Persist metadata in DB
4. On DB failure: best-effort delete written file

Remove workflow:

1. Soft-remove metadata in DB (transaction)
2. Best-effort delete physical file

If physical file is missing but active metadata exists, content route returns not-found. If metadata is removed, content is unavailable even if file cleanup fails.

An orphan-file window exists if the process crashes after file write but before DB commit.

## EXIF / metadata

Original image bytes are stored privately. **EXIF/GPS stripping is NOT implemented** in STEP 14. Future hardening may add metadata stripping with a cross-platform pipeline.

## Customer portal boundary (STEP 16)

Inspection media remains **internal workshop-only**. STEP 16 customer estimate sharing does **not** expose `InspectionMediaAsset` to the customer portal. `/inspection-media/{id}/content` requires `WorkshopOS.Auth` organization membership — not `WorkshopOS.CustomerPortal`.

Customer-facing inspection evidence requires a future explicit visibility/share policy.

## Future

- Cloud storage provider abstraction
- Video / document evidence
- Thumbnails
- Customer-facing inspection media share policy
- Optional per-item photo requirements in custom templates
