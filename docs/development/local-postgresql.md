# Local PostgreSQL Development

WorkshopOS uses an isolated local PostgreSQL instance for development and integration testing.

## Requirements

- PostgreSQL (discovered locally: version 18.x via EnterpriseDB installer)
- `psql` and `pg_isready` available on your PATH (or use the full path to your PostgreSQL `bin` directory)
- .NET 10 SDK
- `dotnet-ef` local tool (`dotnet tool restore`)

## Database names

| Purpose | Database name |
|---|---|
| Development | `workshopos_dev` |
| Integration tests | `workshopos_test` |

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

After provisioning isolated databases and configuring User Secrets:

```bash
dotnet ef database update \
  --project src/WorkshopOS.Infrastructure/WorkshopOS.Infrastructure.csproj \
  --startup-project src/WorkshopOS.Infrastructure/WorkshopOS.Infrastructure.csproj \
  --context AppDbContext
```

The `dotnet ef` command reads the development connection from User Secrets when using the Web or Infrastructure startup project with configured secrets.

Alternatively, set a process-level variable for design-time operations only:

```bash
export WORKSHOPOS_DESIGNTIME_CONNECTION="Host=127.0.0.1;Database=workshopos_dev;Username=workshopos_app;Password=<your-local-secret>"
```

## Run integration tests

```bash
dotnet test WorkshopOS.slnx
```

Tests use transaction rollback for isolation. The test database must already exist and have the `InitialCreate` migration applied.

## Windows note

WorkshopOS code does not depend on macOS-specific PostgreSQL paths. On Windows, provide an equivalent local PostgreSQL connection through User Secrets using the same configuration keys.

Windows verification status: not yet verified in this repository step.

## Safety rules

- Only use `workshopos_dev` and `workshopos_test`
- Do not point WorkshopOS at existing project databases
- Do not store passwords in `appsettings.json` or README files
