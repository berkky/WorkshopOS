# Prerequisites

WorkshopOS **v1.0.0-rc1** source requirements for local development, testing, and packaging.

## Required

| Component | Requirement | Notes |
|-----------|-------------|-------|
| .NET SDK | **10.0.301** (see `global.json`) | `dotnet --version` must satisfy the pinned SDK |
| PostgreSQL | **18.x tested**; PostgreSQL 16+ likely compatible | Not bundled with WorkshopOS |
| Git | Any recent version | For source control workflows |
| Modern browser | Chromium, Firefox, Safari, or Edge | For UI verification |

## Optional but recommended

| Component | Purpose |
|-----------|---------|
| `psql`, `pg_isready` | Database administration and health checks |
| `dotnet-ef` local tool | Installed via `dotnet tool restore` from repository root |

## Not required

- Node.js / npm
- Docker (unless you choose container hosting)
- Playwright / Selenium
- Cloud provider accounts

## Tested development evidence (STEP 21–22)

| Environment | Evidence |
|-------------|----------|
| macOS | Debug/Release build, 657 integration tests, publish, runtime smoke |
| Windows native runtime | **Not executed** in current verification cycle |
| Windows source portability | Source/path audit passed |

## PostgreSQL baseline

Current development verification used **PostgreSQL 18.x** on macOS. WorkshopOS uses standard EF Core + Npgsql; other supported PostgreSQL versions may work but are not guaranteed without your own verification.

## Network

- `dotnet restore` requires NuGet.org (or your configured package feed mirror).
- Primary UI typography (DM Sans) is **self-hosted** under `wwwroot/fonts/dm-sans/` — no Google Fonts runtime dependency.
