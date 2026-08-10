# Local PostgreSQL Development

WorkshopOS uses an isolated local PostgreSQL instance for development and integration testing.

Local PostgreSQL development verified on macOS using PostgreSQL 18.4.

## Requirements

- PostgreSQL 18.x (or compatible)
- `psql` and `pg_isready` available on your PATH
- .NET 10 SDK
- `dotnet-ef` local tool (`dotnet tool restore`)

## Database names

| Purpose | Database name |
|---|---|
| Development | `workshopos_dev` |
| Integration tests | `workshopos_test` |

## Application role

| Role | Purpose |
|---|---|
| `workshopos_app` | Dedicated low-privilege WorkshopOS application login |

Credentials are stored in User Secrets only — never in source control.

## Connection string keys

| Project | Configuration key |
|---|---|
| `WorkshopOS.Web` | `ConnectionStrings:WorkshopOS` |
| `WorkshopOS.Infrastructure.IntegrationTests` | `ConnectionStrings:WorkshopOSTest` |

Secrets must be stored in User Secrets or environment variables. **Do not commit credentials to source control.**

## User Secrets (development)

From the repository root:

```bash
dotnet user-secrets set "ConnectionStrings:WorkshopOS" \
  "Host=127.0.0.1;Port=5432;Database=workshopos_dev;Username=workshopos_app;Password=<your-local-secret>" \
  --project src/WorkshopOS.Web
```

## User Secrets (integration tests)

```bash
dotnet user-secrets set "ConnectionStrings:WorkshopOSTest" \
  "Host=127.0.0.1;Port=5432;Database=workshopos_test;Username=workshopos_app;Password=<your-local-secret>" \
  --project tests/WorkshopOS.Infrastructure.IntegrationTests
```

Integration tests fail fast unless the database name is exactly `workshopos_test` and the host is local.

## Apply migrations (development)

Set `WORKSHOPOS_DESIGNTIME_CONNECTION` from your development User Secret (process-local only — do not commit or print the value), then run:

```bash
dotnet ef database update \
  --project src/WorkshopOS.Infrastructure/WorkshopOS.Infrastructure.csproj \
  --startup-project src/WorkshopOS.Infrastructure/WorkshopOS.Infrastructure.csproj \
  --context AppDbContext
```

Do not use `dotnet user-secrets list` in shared logs — it prints secret values.

## Run integration tests

```bash
dotnet test WorkshopOS.slnx
```

Tests use transaction rollback for isolation. The test database must already exist and have the `InitialCreate` migration applied.

## Windows note

WorkshopOS code does not depend on macOS-specific PostgreSQL paths. Windows development should use an equivalent local PostgreSQL instance and the same configuration key contracts.

Windows verification status: not yet verified in this repository.

## Safety rules

- Only use `workshopos_dev` and `workshopos_test`
- Do not point WorkshopOS at existing project databases
- Do not store passwords in `appsettings.json` or README files
