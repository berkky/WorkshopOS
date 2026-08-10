# Production Configuration

Provider-neutral configuration required before running WorkshopOS in production. **Do not commit secrets.**

## Required settings

| Setting | Description |
|---------|-------------|
| `ConnectionStrings:WorkshopOS` | PostgreSQL connection string (external secret store) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| Public HTTPS origin | TLS termination at reverse proxy or host |
| Data Protection key ring | Persistent, shared across instances/restarts |
| Private media storage | Persistent path outside `wwwroot` with correct permissions |

## Connection string

Store in environment variables, secret manager, or deployment platform — not in source control.

## HTTPS and HSTS

- Terminate TLS at a trusted reverse proxy or the host.
- WorkshopOS enables `UseHttpsRedirection()` and `UseHsts()` in non-Development environments.
- Configure the HTTPS port / forwarded proto headers correctly behind a proxy.

## Trusted reverse proxy

WorkshopOS does **not** enable permissive forwarded-header trust by default.

Before production deployment behind a proxy/load balancer:

1. Identify the trusted proxy network(s).
2. Configure `ForwardedHeadersOptions` to trust only those networks.
3. Never trust arbitrary `X-Forwarded-For` or `X-Forwarded-Proto` from the public internet.

This affects HTTPS scheme detection, HSTS, and IP-based rate limiting.

## Data Protection

ASP.NET Core cookie authentication requires persistent Data Protection keys for:

- Multi-instance deployments
- Process restarts without invalidating all sessions

Configure a shared key ring (file share, database, or platform secret service). Do not commit keys.

## Private inspection media

Default development path: `App_Data/inspection-media/` (outside `wwwroot`).

Production requirements:

- Persistent volume or object storage mount
- Filesystem permissions: application identity read/write only
- Included in backup strategy
- No static file mapping to this directory

See [private media deployment notes](../architecture/inspection-media-v1.md) and deployment runbook.

## Database migrations

Apply migrations explicitly during deployment. Application startup does not migrate.

## Logging

Configure log destination and retention per your operations policy. WorkshopOS does not ship a centralized log aggregator.

## Security headers

Baseline headers are applied by `SecurityHeadersMiddleware`. Customer portal routes apply additional cache and referrer controls.

Global Content-Security-Policy with nonces/hashes is **deferred** — see [security-and-release-readiness-v1.md](../architecture/security-and-release-readiness-v1.md).

## Rate limiting

Login, onboarding, and customer portal exchange endpoints use IP-based rate limits. Ensure client IP reflects the real client when behind a proxy (requires correct forwarded header configuration).

## Environment-specific files

`appsettings.json` contains non-secret defaults. Production secrets belong in environment variables or your secret manager — not User Secrets.
