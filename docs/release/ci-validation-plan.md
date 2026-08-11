# CI Validation Plan

Future continuous-integration design for WorkshopOS technical release verification. **No remote CI is active** as of STEP 28. This document prepares jobs for when the owner authorizes git push and runner access.

## Goals

| Goal | Rationale |
|------|-----------|
| Prove reproducible build/test on every change | Catch regressions before release |
| Separate cross-publish from native Windows proof | Avoid false confidence from macOS-only `win-x64` publish |
| Keep secrets out of CI logs | Production and dev credentials must never print |
| No automatic deployment | CI validates; it does not ship |

## Proposed workflow jobs

### A. Linux + PostgreSQL — full test suite

| Item | Detail |
|------|--------|
| Runner | `ubuntu-latest` |
| Services | PostgreSQL 16+ container |
| Steps | `dotnet tool restore` → `dotnet restore` → build (Debug, Release, `-warnaserror`) → `dotnet test` |
| Env | `ConnectionStrings__WorkshopOSTest` from encrypted CI secret |
| EF check | `dotnet ef migrations has-pending-model-changes` with `WORKSHOPOS_DESIGNTIME_CONNECTION` |
| Vulnerability | `dotnet list package --vulnerable` (fail on findings) |

This job is the **authoritative integration-test gate** including DB-backed readiness logic exercised via tests.

### B. macOS — build, publish, runtime smoke

| Item | Detail |
|------|--------|
| Runner | `macos-latest` |
| Steps | Restore → Release build → `dotnet publish` (framework-dependent) → start published binary → curl `/`, `/account/login`, `/health/live`, `/health/ready` |
| DB | Optional secret for readiness smoke; liveness probes require no DB |
| Artifact | Upload publish folder as short-lived workflow artifact (not a release) |

### C. Windows — native build, publish, runtime smoke

| Item | Detail |
|------|--------|
| Runner | `windows-latest` |
| Steps | Restore → Release build → `dotnet publish -r win-x64 --self-contained false` → `scripts/release/Test-WorkshopOS-Windows.ps1` |
| Mandatory probes | `/`, `/account/login`, `/health/live` (no DB) |
| Readiness | Optional `-TestReadiness` when `ConnectionStrings__WorkshopOS` secret configured |
| Artifact | Upload `win-x64` publish folder |

This job provides **native Windows execution evidence** that cross-publish cannot.

### D. Vulnerability audit

| Item | Detail |
|------|--------|
| Runner | Any (`ubuntu-latest` sufficient) |
| Command | `dotnet list WorkshopOS.slnx package --vulnerable` |
| Policy | Fail workflow on known vulnerable packages |

### E. Source package audit

| Item | Detail |
|------|--------|
| Runner | `ubuntu-latest` or `macos-latest` |
| Steps | Create clean source snapshot (exclude `.git`, `bin`, `obj`, `.visual-qa`, `App_Data`) → secret pattern scan → extract → restore/build/test → publish proof |
| Output | SHA-256 + file count logged to workflow summary (not committed) |

## Secrets policy

| Secret | Usage |
|--------|-------|
| `ConnectionStrings__WorkshopOSTest` | Job A integration tests only |
| `ConnectionStrings__WorkshopOS` | Optional Jobs B/C readiness smoke only |
| `WORKSHOPOS_DESIGNTIME_CONNECTION` | EF drift check (test database) |

Never log secret values. Mask secrets in GitHub Actions.

## Local workflow file

A draft workflow may exist at `.github/workflows/validate.yml` for owner review. It is **not pushed or executed** until authorized.

## Triggers (future)

| Trigger | Purpose |
|---------|---------|
| `pull_request` | Pre-merge validation |
| `workflow_dispatch` | Manual release-candidate rehearsal |
| `push` to `main` | Post-merge health (optional) |

## Explicit non-goals

- No NuGet/package publication
- No GitHub Release creation
- No production deployment
- No database migration against production
- No license or brand finalization

## Current status

| Item | Status |
|------|--------|
| CI workflow in repository | Draft only (not pushed) |
| Remote execution | **Not active** |
| Native Windows proof | Requires Job C on `windows-latest` |
