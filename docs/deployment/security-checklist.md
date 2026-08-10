# Security Deployment Checklist

Pre-production security verification for WorkshopOS v1.0.0-rc1. This is a **controls checklist**, not a compliance certification.

## Environment

- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Debug/developer exception pages disabled
- [ ] No User Secrets in production host

## Transport

- [ ] HTTPS enforced for all public traffic
- [ ] HSTS enabled (non-Development pipeline)
- [ ] Valid TLS certificates

## Reverse proxy

- [ ] Trusted proxy networks explicitly configured
- [ ] Arbitrary `X-Forwarded-*` headers not trusted from internet clients
- [ ] Client IP for rate limits reflects real client when proxied

## Secrets

- [ ] Database credentials in secret manager only
- [ ] No connection strings in source or publish output
- [ ] No raw estimate share tokens in logs

## Data Protection

- [ ] Persistent key ring configured for multi-instance/restart safety
- [ ] Keys backed up securely

## Database

- [ ] Migrations applied explicitly (not at startup)
- [ ] Least-privilege DB role
- [ ] Automated backups configured and restore tested

## Private media

- [ ] Storage outside `wwwroot`
- [ ] Filesystem permissions restricted
- [ ] Media included in backup strategy
- [ ] No `StorageKey` exposed in HTML

## Application security baseline

- [ ] Staff and Customer Portal auth schemes isolated
- [ ] CSRF on all cookie-auth mutations
- [ ] Login rate limiting active
- [ ] Onboarding and portal exchange rate limits active
- [ ] Security headers middleware active
- [ ] Portal routes: `no-referrer`, `no-store`, `frame-ancestors 'none'`

## Customer portal shares

- [ ] Share secrets delivered via URL fragment only
- [ ] Raw token never persisted (hash only)
- [ ] Revocation and expiry enforced on every request

## Dependencies

- [ ] `dotnet list package --vulnerable` clean at deploy time

## Operational

- [ ] Log retention and access controls defined
- [ ] No payment card data collected (ledger metadata only)

## Not claimed

WorkshopOS does **not** certify PCI DSS, SOC 2, ISO 27001, HIPAA, GDPR, KVKK, tax, or e-invoice compliance.
