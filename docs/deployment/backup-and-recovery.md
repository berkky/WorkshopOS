# Backup and Recovery

WorkshopOS does **not** perform automatic backups. Deployers must implement backup and recovery.

## What to back up

| Asset | Why |
|-------|-----|
| PostgreSQL database | All business data, identity, shares (hashed), inventory, invoices |
| Private inspection media | Photo evidence stored outside `wwwroot` |
| Data Protection keys | Required to decrypt existing auth cookies after restore |

## PostgreSQL

Use your provider’s standard backup tooling (`pg_dump`, managed snapshots, PITR, etc.).

**Recommendation:** daily automated backups minimum; test restores regularly.

## Private media

Back up the configured inspection media root directory (default `App_Data/inspection-media/` in development).

Media files are referenced by opaque storage keys in the database — restore both DB and files together.

## Data Protection keys

Include ASP.NET Core Data Protection key ring in your secret/backup strategy. Losing keys invalidates existing authentication cookies.

## Recovery procedure (outline)

1. Restore PostgreSQL to a consistent point in time.
2. Restore private media files to the configured path.
3. Restore Data Protection keys to the application configuration.
4. Deploy matching application version (same migration set).
5. Run smoke tests (login, staff route, portal bootstrap page, media content route).
6. Verify recent repair orders, estimates, and invoices.

## RPO / RTO

Recovery Point Objective and Recovery Time Objective are **defined by the deployer**, not WorkshopOS.

## WorkshopOS scope

WorkshopOS provides data integrity constraints and immutable audit-friendly ledgers. It does not include disaster-recovery orchestration, geo-replication, or backup scheduling.
