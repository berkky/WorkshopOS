# Testing Guide

## Test project

`tests/WorkshopOS.Infrastructure.IntegrationTests`

Framework: **xUnit** with real **PostgreSQL** integration (no in-memory database substitute for business workflows).

## Requirements

- `workshopos_test` database on local PostgreSQL
- `ConnectionStrings:WorkshopOSTest` in User Secrets or environment variables
- `PostgreSqlConnectionGuard` enforces database name and local host

## Transaction rollback

Each test runs inside a database transaction that rolls back after the test completes. This keeps `workshopos_test` clean without `TRUNCATE` or `EnsureDeleted` between tests.

## Run full suite

```bash
dotnet test WorkshopOS.slnx
```

## Release candidate baseline

**657 tests** passed at v1.0.0-rc1 technical verification (620 STEP 21 foundation baseline + 37 STEP 21–23 additions). Future development may increase this count.

## Build before test (recommended)

```bash
dotnet build WorkshopOS.slnx
dotnet test WorkshopOS.slnx --no-build
```

## Release warnings-as-errors

```bash
dotnet build WorkshopOS.slnx -c Release -warnaserror
dotnet test WorkshopOS.slnx --no-build
```

## What tests cover

- Tenant isolation and composite foreign keys
- Authentication and authorization policies
- Customer portal share security
- Inspection media privacy
- Estimate/invoice/payment commercial boundaries
- Inventory ledger invariants
- End-to-end commercial lifecycle golden paths
- Security release readiness source contracts

## Clean source package boundary

Full PostgreSQL integration tests are verified from the main repository with configured `workshopos_test` secrets. A clean extracted source package is verified for restore/build/publish; full DB tests in the package environment require the operator to configure `workshopos_test` separately without copying secret values into the package.
