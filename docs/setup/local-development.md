# Local Development Setup

Step-by-step guide for a fresh WorkshopOS developer machine. **Do not commit secrets.**

## 1. Obtain source

Clone or copy the WorkshopOS source tree. The technical release candidate includes all `src/`, `tests/`, `docs/`, and solution files.

## 2. Enter repository root

```bash
cd WorkshopOS
```

## 3. Install .NET SDK

Install **.NET SDK 10.0.301** (or compatible patch per `global.json`).

```bash
dotnet --version
```

## 4. Restore local tools

```bash
dotnet tool restore
```

This installs `dotnet-ef` per `dotnet-tools.json`.

## 5. Configure PostgreSQL

Create isolated local databases:

| Database | Purpose |
|----------|---------|
| `workshopos_dev` | Development |
| `workshopos_test` | Integration tests |

Create a dedicated application role (example name: `workshopos_app`) with access only to these databases.

See [docs/development/local-postgresql.md](../development/local-postgresql.md) for role and provisioning details.

## 6. Configure User Secrets

**Web application:**

```bash
dotnet user-secrets set "ConnectionStrings:WorkshopOS" \
  "<your-local-connection-string>" \
  --project src/WorkshopOS.Web
```

**Integration tests:**

```bash
dotnet user-secrets set "ConnectionStrings:WorkshopOSTest" \
  "<your-local-connection-string>" \
  --project tests/WorkshopOS.Infrastructure.IntegrationTests
```

The test connection string **must** target database `workshopos_test` on a local host. Tests fail fast otherwise.

## 7. Restore packages

```bash
dotnet restore WorkshopOS.slnx
```

## 8. Apply migrations (development database)

Application startup **does not** migrate automatically. Apply migrations explicitly:

```bash
export WORKSHOPOS_DESIGNTIME_CONNECTION="<same-as-dev-connection-string>"
dotnet ef database update \
  --project src/WorkshopOS.Infrastructure \
  --startup-project src/WorkshopOS.Infrastructure
```

See [database-migrations.md](database-migrations.md).

## 9. Build

```bash
dotnet build WorkshopOS.slnx
```

## 10. Run tests

```bash
dotnet test WorkshopOS.slnx
```

Requires `workshopos_test` and uses transaction rollback — see [testing.md](testing.md).

## 11. Run Web application

```bash
dotnet run --project src/WorkshopOS.Web
```

Open the URL shown in the console (commonly `http://localhost:5xxx`).

## 12. First-time owner onboarding

Navigate to `/onboarding` and create the first workshop organization and owner account through the normal UI. No demo seed runs at startup.

## Windows notes

Commands are identical in PowerShell or Windows Terminal. Use your PostgreSQL installation’s connection string format. Paths are resolved by .NET — avoid hard-coded drive letters in configuration.

## macOS notes

PostgreSQL installation method is your choice (installer, package manager, or container). macOS build/test/publish/runtime smoke verified in STEP 21–22.
