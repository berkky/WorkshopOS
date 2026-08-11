# WorkshopOS SBOM Strategy

**Purpose:** Define how Software Bill of Materials (SBOM) will be produced for WorkshopOS releases.  
**Status:** Strategy only — **no SBOM is generated in STEP 24**.

---

## Preferred format: SPDX

**SPDX (Software Package Data Exchange)** is the preferred SBOM format because it is:

- Standardized and widely adopted
- Machine-readable
- Suitable for supply-chain and license compliance review
- Compatible with common security/compliance tooling ecosystems

Alternative formats (e.g., CycloneDX) may be evaluated later, but SPDX is the default preference.

---

## Scope of a WorkshopOS SBOM

| Included | Notes |
|----------|-------|
| Direct NuGet dependencies | From solution projects |
| Significant transitive dependencies | Especially security/licensing relevance |
| Bundled front-end libraries | Bootstrap, jQuery, validation bundles |
| Self-hosted fonts | DM Sans (OFL) — note separately from proprietary code |
| WorkshopOS proprietary components | Marked as proprietary / NOASSERTION until license ID finalized |

---

## Candidate generation approaches (no new tools installed in STEP 24)

Use existing .NET dependency information at release time:

```bash
# Inventory direct packages per project
dotnet list WorkshopOS.slnx package

# Include transitive graph
dotnet list WorkshopOS.slnx package --include-transitive

# Vulnerability correlation (separate security gate)
dotnet list WorkshopOS.slnx package --vulnerable --include-transitive
```

Future release closure may add a dedicated SPDX generator tool **after** owner approval and toolchain evaluation. Candidate options to assess later:

- `dotnet sbom` ecosystem tools (when approved for release pipeline)
- SPDX tooling converting `dotnet list` output
- CI-integrated SBOM generation on tagged releases

---

## When SBOM is produced

| Phase | SBOM action |
|-------|-------------|
| RC technical verification (now) | Strategy only |
| Canonical release commit/tag | **Generate SBOM** from locked dependency graph |
| Customer distribution package | Attach SBOM alongside `THIRD-PARTY-NOTICES.md` |

SBOM generation belongs to **release closure after source commit is canonical**, not to uncommitted working-tree states.

---

## Relationship to THIRD-PARTY-NOTICES.md

| Document | Role |
|----------|------|
| `THIRD-PARTY-NOTICES.md` | Human-readable license summary for buyers |
| SBOM (SPDX) | Machine-readable component inventory for automation/compliance |

Both must remain consistent. SBOM does not replace legal review of third-party obligations.

---

## Proprietary component representation

Until final license SPDX ID is defined by counsel, WorkshopOS proprietary packages may be recorded with:

- `NOASSERTION` or counsel-approved proprietary identifier
- Clear separation from MIT/Apache/OFL components

Do not fabricate a formal SPDX license ID for WorkshopOS without legal approval.
