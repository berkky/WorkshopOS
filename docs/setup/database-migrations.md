# Database Migrations

WorkshopOS uses EF Core migrations in `src/WorkshopOS.Infrastructure/Persistence/Migrations/`.

## Current state (v1.0.0-rc1)

| Item | Value |
|------|-------|
| Migration history | **9** |
| Pending migrations | **0** |
| Pending model changes | **NO** |

## Why startup does not migrate

`WorkshopOS.Web` intentionally does **not** call `Database.Migrate()` or `EnsureCreated()` at startup. Database schema changes are an explicit deployment activity so production rollouts remain controlled and auditable.

## Design-time DbContext

EF CLI uses `WORKSHOPOS_DESIGNTIME_CONNECTION` when the startup project is `WorkshopOS.Infrastructure`:

```bash
export WORKSHOPOS_DESIGNTIME_CONNECTION="<your-local-connection-string>"
```

Do not commit this value.

## Inspect pending model changes

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/WorkshopOS.Infrastructure \
  --startup-project src/WorkshopOS.Infrastructure
```

Expected: `No changes have been made to the model since the last migration.`

## List migrations

```bash
dotnet ef migrations list \
  --project src/WorkshopOS.Infrastructure \
  --startup-project src/WorkshopOS.Infrastructure
```

## Apply migrations (development)

```bash
dotnet ef database update \
  --project src/WorkshopOS.Infrastructure \
  --startup-project src/WorkshopOS.Infrastructure
```

## Production deployment

1. Take a database backup before migration.
2. Run `dotnet ef database update` (or equivalent CI/CD step) against the production connection string.
3. Deploy the application build that matches the migration set.
4. Verify application health and smoke routes.

Never rely on application startup to apply schema changes in production.

## Databases

| Name | Purpose |
|------|---------|
| `workshopos_dev` | Local development |
| `workshopos_test` | Integration tests only |

Do not point development or test configurations at production databases.
