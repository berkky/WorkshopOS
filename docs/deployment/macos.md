# macOS deployment

Host-neutral framework-dependent deployment guide for WorkshopOS on macOS (Apple Silicon or Intel).

## Requirements

| Component | Version |
|-----------|---------|
| .NET runtime | **ASP.NET Core 10.0** (matches SDK **10.0.301** in `global.json`) |
| Database | PostgreSQL reachable over **TCP** |
| Reverse proxy | Recommended for TLS (nginx, Caddy, cloud load balancer) |

WorkshopOS does **not** require launchd. Any process manager that can host an ASP.NET Core Kestrel application is acceptable.

## Publish (framework-dependent)

```bash
dotnet restore WorkshopOS.slnx
dotnet publish src/WorkshopOS.Web/WorkshopOS.Web.csproj \
  -c Release \
  -o /var/lib/workshopos/publish
```

No `RuntimeIdentifier` is required for a portable macOS framework-dependent publish when building on macOS.

Copy the publish folder to the target host. Do **not** copy User Secrets, `.env`, or developer `App_Data` content.

## Configuration

### Required settings

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:WorkshopOS` | PostgreSQL connection (network host/port) |
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` in production |

Set via environment variables or your secret manager:

- `ConnectionStrings__WorkshopOS`
- `ASPNETCORE_ENVIRONMENT`

User Secrets are **development-only** and are not deployed with publish output.

### Optional settings

| Key | Purpose |
|-----|---------|
| `InspectionMedia:StorageRootPath` | Persistent private media directory (must be outside `wwwroot`) |

If omitted, the application defaults to `{ContentRoot}/App_Data/inspection-media`.

### Data Protection

Configure persistent ASP.NET Data Protection key storage shared by all instances. Keys must survive restarts.

See [production-configuration.md](production-configuration.md) and [deployment-runbook.md](deployment-runbook.md).

## PostgreSQL connectivity

Use standard Npgsql TCP configuration (hostname, port, database, credentials). Unix-domain sockets are not required by the application.

Apply migrations separately; the web application does **not** auto-migrate at startup. See [database-migrations.md](../setup/database-migrations.md).

## Private inspection media

- Default path: `{ContentRoot}/App_Data/inspection-media`
- Override with `InspectionMedia:StorageRootPath`
- Must remain **outside** `wwwroot`
- Ensure the application user can read/write the directory
- Include in backup scope alongside PostgreSQL

No Unix-specific permission model (e.g. `chmod`) is required for application correctness.

## Startup

```bash
cd /var/lib/workshopos/publish
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__WorkshopOS="<from secret manager>"
dotnet WorkshopOS.Web.dll --urls "http://127.0.0.1:5000"
```

Bind to an internal port; expose HTTPS through your reverse proxy.

## Service hosting (conceptual)

| Option | Notes |
|--------|-------|
| launchd | plist wrapping `dotnet WorkshopOS.Web.dll` |
| Container | Publish folder in a Linux/macOS container with ASP.NET runtime base image |
| Manual | Development or single-node deployments |

## Reverse proxy / HTTPS

Terminate TLS at the proxy. Forward `X-Forwarded-For` and `X-Forwarded-Proto` only from trusted networks.

## Smoke verification

After deploy (GET only; no startup migration):

| Route | Expected |
|-------|----------|
| `/` | 200 |
| `/account/login` | 200 |
| `/health/live` | 200 — process liveness (no database probe) |
| `/health/ready` | 200 when PostgreSQL is reachable; 503 when not |

Use `/health/live` for process/orchestrator liveness checks. Use `/health/ready` for load-balancer readiness after database connectivity is required.

## Development note

STEP 26 verified native macOS build, test, publish, and runtime smoke on the maintainer environment. See [Windows deployment](windows.md) for cross-publish vs native execution distinctions.

## Related documentation

- [deployment-runbook.md](deployment-runbook.md)
- [production-configuration.md](production-configuration.md)
- [security-checklist.md](security-checklist.md)
- [Windows deployment](windows.md)
