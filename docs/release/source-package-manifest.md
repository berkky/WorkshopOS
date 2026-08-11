# Source Package Manifest — v1.0.0-rc1 (STEP 28 Technical RC)

## Package identity

| Item | Value |
|------|-------|
| Label | WorkshopOS v1.0.0-rc1 — **technical RC (STEP 28)** |
| Filename | `WorkshopOS-v1.0.0-rc1-technical-step28-source.zip` |
| Location | `/tmp/workshopos-step28/` (ephemeral, outside repository) |
| SHA-256 | `15fc99412c05cd2db9a116e6544657809cbfca90cd3cd8934ed07bc4fb5b4c42` |
| File count | **543** source files |
| Size | ~2.9 MB (compressed) |

**This is NOT final commercial distribution.** Final brand clearance, proprietary LICENSE, monitored security contact, and canonical release commit/tag remain blockers.

## STEP 28 verification summary

| Check | Result |
|-------|--------|
| Extracted restore/build (Debug, Release, `-warnaserror`) | 0 errors / 0 warnings |
| Extracted full test suite | **684 / 684 PASS** |
| Extracted framework-dependent publish | PASS |
| Secret pattern scan (pre-ZIP) | CLEAN (documentation/test literals only) |
| Content audit (no `.git`, `.visual-qa`, `App_Data`) | PASS |
| SDK | **10.0.301** (`global.json`) |
| Migrations (source) | **9** |
| Pending model changes | **No** |
| macOS native publish/runtime | PASS |
| Windows `win-x64` cross-publish | PASS (PE32+ verified) |
| Windows native execution | **NOT YET VERIFIED** (harness ready) |
| Authenticated browser QA | **PENDING / SAFELY BLOCKED** |
| Vulnerable NuGet packages | None |

## Working tree inventory (STEP 28 audit)

| Category | Modified | Untracked | Notes |
|----------|----------|-----------|-------|
| Product source | 52 | 9 | Views, controllers, health, CSS, shared partials |
| Test source | 2 | 3 | Pagination, health, portability tests |
| Documentation | 2 | 4 | Deployment guides, release docs |
| Release governance | 0 | 18 | `docs/release/**` (trackable) |
| Deployment | 0 | 2 | `docs/deployment/macos.md`, `windows.md` |
| Release engineering | 0 | 2 | `scripts/release/`, `.github/workflows/` |
| Static asset | 2 | 0 | `workshopos-components.css`, `workshopos-shell.css` |
| Migration | 0 | 0 | Unchanged (9 in source) |
| QA artifact | 0 | 0 | `.visual-qa/` gitignored |
| Build artifact | 0 | 0 | `bin/`/`obj/` gitignored |
| Git/line-ending policy | 0 | 1 | `.gitattributes` |
| **UNKNOWN** | **0** | **0** | All files classified |

## Included

| Path | Purpose |
|------|---------|
| `src/` | Application source (Domain, Application, Infrastructure, Web) |
| `tests/` | Integration test suite (684 tests) |
| `docs/` | Setup, deployment, architecture, demo, product, release docs |
| `scripts/release/` | Windows native validation harness |
| `.github/workflows/` | Draft CI validation workflow (not pushed) |
| `WorkshopOS.slnx` | Solution file |
| `global.json` | SDK pin (10.0.301) |
| `dotnet-tools.json` | `dotnet-ef` local tool manifest |
| `README.md` | Product entry point |
| `SECURITY.md` | Security policy |
| `THIRD-PARTY-NOTICES.md` | Dependency and vendor licenses |
| `.gitignore` / `.gitattributes` | Repository hygiene |
| `src/WorkshopOS.Infrastructure/Persistence/Migrations/` | EF migration source (9 migrations) |
| `src/WorkshopOS.Web/wwwroot/` | Self-hosted fonts, design CSS/JS, hero SVGs |

## Excluded

| Item | Reason |
|------|--------|
| `.git/` | Version control metadata |
| `bin/`, `obj/` | Build output |
| `TestResults/`, coverage | Test artifacts |
| `.visual-qa/` | Screenshot QA artifacts |
| `App_Data/inspection-media/` | Runtime uploaded evidence |
| User Secrets | Machine-specific credentials |
| Real connection strings | Secrets |
| Private keys | Security |
| Publish output | Generated at deploy time |
| IDE caches (`.vs/`, `.idea/`) | Developer-local |
| Root `LICENSE` | Intentionally absent — legal review pending |

## Required external configuration

| Setting | Location |
|---------|----------|
| `ConnectionStrings:WorkshopOS` | User Secrets / environment |
| `ConnectionStrings:WorkshopOSTest` | Test project User Secrets |
| `WORKSHOPOS_DESIGNTIME_CONNECTION` | EF CLI (process-local) |
| Data Protection keys | Production deployment |
| `InspectionMedia:StorageRootPath` | Optional; defaults to `{ContentRoot}/App_Data/inspection-media` |

## First-run steps (summary)

1. `dotnet tool restore`
2. Configure PostgreSQL (`workshopos_dev`, `workshopos_test`)
3. Configure User Secrets (no values in package)
4. `dotnet restore WorkshopOS.slnx`
5. `dotnet ef database update` (explicit — startup does not auto-migrate)
6. `dotnet build WorkshopOS.slnx`
7. `dotnet test WorkshopOS.slnx` (with test DB configured)
8. `dotnet run --project src/WorkshopOS.Web`
9. `/onboarding` for first owner

## Health endpoints

| Route | Purpose |
|-------|---------|
| `GET /health/live` | Process liveness (no DB) |
| `GET /health/ready` | PostgreSQL readiness (`SELECT 1`, 5s timeout) |

## Windows native validation

Harness: `scripts/release/Test-WorkshopOS-Windows.ps1`  
Guide: `docs/release/windows-native-validation.md`

## SBOM

SPDX SBOM generation **deferred** per `docs/release/sbom-strategy.md`. Machine-readable dependency inventory available at `/tmp/workshopos-step28/dependency-inventory.json` (QA artifact, not in package).

## License notice

WorkshopOS **project license is not defined** in this package. Commercial distribution requires owner license decision. Third-party components are listed in `THIRD-PARTY-NOTICES.md`.

## Supersedes

STEP 23.x source ZIP — stale after STEPS 24–27 product and release-engineering improvements.
