# Deployment Runbook

Provider-neutral production deployment stages for WorkshopOS v1.0.0-rc1.

## 1. Provision PostgreSQL

- Create production database (dedicated instance recommended).
- Create least-privilege application role.
- Store connection string in secret manager.

## 2. Configure secrets

Set `ConnectionStrings:WorkshopOS` and `ASPNETCORE_ENVIRONMENT=Production` via your platform’s secret mechanism.

## 3. Migrate database

```bash
export WORKSHOPOS_DESIGNTIME_CONNECTION="<production-connection-string>"
dotnet ef database update \
  --project src/WorkshopOS.Infrastructure \
  --startup-project src/WorkshopOS.Infrastructure
```

Or equivalent CI/CD migration job. **Backup first.**

## 4. Configure Data Protection keys

Provision persistent key storage shared by all application instances. Verify keys survive restarts.

## 5. Configure private media storage

- Create persistent directory or mount (example: `/var/lib/workshopos/inspection-media`).
- Set appropriate filesystem permissions.
- Ensure path is outside web root and not publicly mapped.

## 6. Configure trusted reverse proxy

If using nginx, IIS, Cloudflare, AWS ALB, Azure App Gateway, or similar:

- Terminate TLS at the proxy.
- Forward `X-Forwarded-For` / `X-Forwarded-Proto` only from trusted networks.
- Configure WorkshopOS forwarded headers before go-live (deployment-specific).

## 7. Publish application

```bash
dotnet publish src/WorkshopOS.Web/WorkshopOS.Web.csproj \
  -c Release \
  -o /path/to/publish
```

## 8. Start process

Run `WorkshopOS.Web` behind your process manager (systemd, Windows Service, platform host). Bind to internal port; expose via proxy.

## 9. Smoke verification

GET (no DB writes required for basic smoke):

| Route | Expected |
|-------|----------|
| `/` | 200 |
| `/account/login` | 200 |
| `/onboarding` | 200 |
| `/dashboard` | Staff auth challenge |
| `/portal/estimate` | Customer portal challenge |
| `/css/workshopos-public.css` | 200 |
| `/js/workshopos-hero.js` | 200 |

## 10. Backup validation

Verify PostgreSQL backup and private media backup jobs run successfully. Perform a restore drill in a non-production environment.

## 11. Monitor logs

Watch authentication failures, rate-limit events, and unhandled exceptions. No stack traces should reach end users in Production.

## Post-deploy verification

- [ ] HTTPS only for public access
- [ ] Migrations applied
- [ ] Data Protection keys persistent
- [ ] Media storage writable
- [ ] Backups scheduled
- [ ] Dependency vulnerability audit in CI

See [security-checklist.md](security-checklist.md) and [release-checklist.md](../release/release-checklist.md).
