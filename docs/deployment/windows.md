# Windows deployment

Host-neutral framework-dependent deployment guide for WorkshopOS on Windows Server or Windows desktop environments.

## Requirements

| Component | Version |
|-----------|---------|
| .NET runtime | **ASP.NET Core 10.0** (matches SDK **10.0.301** in `global.json`) |
| Database | PostgreSQL reachable over **TCP** (hostname, port, database, credentials) |
| Reverse proxy | Recommended for TLS termination (IIS, nginx, Caddy, cloud load balancer) |

WorkshopOS does **not** require IIS. Any process manager that can host an ASP.NET Core Kestrel application is acceptable.

## Publish (framework-dependent)

From a machine with the .NET SDK installed:

```powershell
dotnet restore WorkshopOS.slnx
dotnet publish src/WorkshopOS.Web/WorkshopOS.Web.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o C:\publish\workshopos
```

If restore reports missing `net10.0/win-x64` assets, run a targeted RID restore:

```powershell
dotnet restore src/WorkshopOS.Web/WorkshopOS.Web.csproj -r win-x64
```

Then repeat `dotnet publish` with `--no-restore` if desired.

Copy the publish folder to the target server. Do **not** copy User Secrets, `.env`, or developer `App_Data` content.

## Configuration

### Required settings

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:WorkshopOS` | PostgreSQL connection (network host/port; no Unix socket required) |
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` in production |

Set via environment variables (double underscore nesting), Windows environment UI, or your secret manager. Example variable names:

- `ConnectionStrings__WorkshopOS`
- `ASPNETCORE_ENVIRONMENT`

User Secrets (`dotnet user-secrets`) are **development-only** and are not deployed with publish output.

### Optional settings

| Key | Purpose |
|-----|---------|
| `InspectionMedia:StorageRootPath` | Persistent private media directory (must be outside `wwwroot`) |

If omitted, the application defaults to `{ContentRoot}/App_Data/inspection-media`.

### Data Protection

Configure persistent ASP.NET Data Protection key storage shared by all instances. Keys must survive restarts and be readable by the application identity. Without this, cookie authentication and antiforgery tokens invalidate after redeploy.

See [production-configuration.md](production-configuration.md) and [deployment-runbook.md](deployment-runbook.md).

## PostgreSQL connectivity

Use standard Npgsql TCP configuration:

- Hostname or IP (not a Unix-domain socket path)
- Port (default `5432`)
- Database name
- Username and password
- TLS settings as required by your environment

Apply migrations **before** or as part of deployment using the design-time factory and `WORKSHOPOS_DESIGNTIME_CONNECTION` (see [database-migrations.md](../setup/database-migrations.md)). The web application does **not** auto-migrate at startup.

## Private inspection media

- Default path: `{ContentRoot}\App_Data\inspection-media`
- Override with `InspectionMedia:StorageRootPath`
- Must remain **outside** `wwwroot`
- Grant the application identity read/write access to the directory
- Include in backup scope alongside PostgreSQL

## Startup

```powershell
cd C:\publish\workshopos
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ConnectionStrings__WorkshopOS = "<from secret manager>"
dotnet WorkshopOS.Web.dll --urls "http://127.0.0.1:5000"
```

Bind to an internal port; expose HTTPS through your reverse proxy.

## Service hosting (conceptual)

| Option | Notes |
|--------|-------|
| Windows Service | Wrap `dotnet WorkshopOS.Web.dll` with a service manager (e.g. NSSM, `sc.exe` + wrapper) |
| IIS | Use ASP.NET Core Module in-process or out-of-process |
| Container | Publish folder in a Windows container with ASP.NET runtime base image |

## Reverse proxy / HTTPS

Terminate TLS at the proxy. Forward `X-Forwarded-For` and `X-Forwarded-Proto` only from trusted networks. Configure forwarded headers in deployment per [deployment-runbook.md](deployment-runbook.md).

## Smoke verification

After deploy (GET only; no startup migration):

| Route | Expected |
|-------|----------|
| `/` | 200 |
| `/account/login` | 200 |
| `/health/live` | 200 — process liveness (no database probe) |
| `/health/ready` | 200 when PostgreSQL is reachable; 503 when not |

Use `/health/live` for process/orchestrator liveness checks. Use `/health/ready` for load-balancer readiness after database connectivity is required.

## Cross-platform note

Building and publishing `win-x64` artifacts can be performed on macOS or Linux with the .NET SDK (cross-publish). **Native Windows runtime execution** must be verified separately on a trusted Windows host or CI runner.

## Related documentation

- [deployment-runbook.md](deployment-runbook.md)
- [production-configuration.md](production-configuration.md)
- [security-checklist.md](security-checklist.md)
- [macOS deployment](macos.md)
