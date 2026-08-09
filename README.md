# WorkshopOS

Premium automotive workshop management SaaS platform.

## Current status

Foundation stage with persistence layer implemented. Local PostgreSQL provisioning and migration application are required before runtime database access and integration tests can run.

## Requirements

- .NET 10 SDK
- Local PostgreSQL (for development and integration tests)

## Development

```bash
dotnet tool restore
dotnet restore
dotnet build WorkshopOS.slnx
dotnet run --project src/WorkshopOS.Web
dotnet test
```

Configure database connection strings via User Secrets — do not commit credentials to source control.

| Project | Configuration key |
|---|---|
| `WorkshopOS.Web` | `ConnectionStrings:WorkshopOS` |
| `WorkshopOS.Infrastructure.IntegrationTests` | `ConnectionStrings:WorkshopOSTest` |

See [docs/development/local-postgresql.md](docs/development/local-postgresql.md) for provisioning, migration, and test setup.

## Database

PostgreSQL integration uses isolated local databases (`workshopos_dev`, `workshopos_test`). The `InitialCreate` migration is generated but not yet applied until databases are provisioned.

## Cross-platform

Development targets macOS and Windows using standard .NET CLI workflows.
