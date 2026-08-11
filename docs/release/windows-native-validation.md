# Windows Native Validation

Guide for proving **native Windows runtime execution** on a trusted Windows host or CI runner. This is separate from cross-publishing `win-x64` artifacts from macOS/Linux.

## Status distinction

| Evidence | STEP 28 status |
|----------|----------------|
| `win-x64` cross-publish from macOS | **VERIFIED** |
| Native Windows execution (`WorkshopOS.Web.exe`) | **NOT YET NATIVE VERIFIED** |

Cross-publish proves packaging and PE artifact shape. Only a trusted Windows environment can prove native execution.

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| Windows Server 2019+ or Windows 10/11 x64 | Trusted operator or CI runner |
| ASP.NET Core 10.0 runtime | Matches SDK **10.0.301** in `global.json` |
| PowerShell 5.1+ | Built into modern Windows |
| Published artifact | Framework-dependent `win-x64` folder from `dotnet publish` |
| PostgreSQL (optional) | Required only for `/health/ready` acceptance |

## Artifact

Use a framework-dependent publish output, for example:

```
C:\validate\workshopos\
  WorkshopOS.Web.exe
  WorkshopOS.Web.dll
  wwwroot\
  appsettings.json
  ...
```

Obtain the artifact from:

- A Windows `dotnet publish -r win-x64 --self-contained false` build, or
- A cross-published artifact verified on macOS (STEP 26/28) copied to the Windows host.

Do **not** copy User Secrets, `.env`, developer `App_Data`, or local database files.

## PowerShell harness

Script: [`scripts/release/Test-WorkshopOS-Windows.ps1`](../../scripts/release/Test-WorkshopOS-Windows.ps1)

### Mandatory probes (no database required)

```powershell
.\scripts\release\Test-WorkshopOS-Windows.ps1 `
  -PublishPath C:\validate\workshopos
```

The harness:

1. Starts `WorkshopOS.Web.exe` on `http://127.0.0.1:5928` (override with `-Port`)
2. Waits up to 30 seconds for `/health/live`
3. Probes:

| Route | Expected |
|-------|----------|
| `/` | 200 |
| `/account/login` | 200 |
| `/health/live` | 200 |

4. Stops only the process it started
5. Returns exit code `0` on success, `1` on failure

The harness does **not** print connection strings, passwords, or tokens.

### Optional readiness probe (database required)

Configure connectivity **before** running the harness (operator-supplied secret manager or environment variable):

```powershell
$env:ConnectionStrings__WorkshopOS = '<from secret manager>'
.\scripts\release\Test-WorkshopOS-Windows.ps1 `
  -PublishPath C:\validate\workshopos `
  -TestReadiness
```

Readiness result is reported only as:

- `ready PASS`, or
- `ready FAIL`

No database exception text or connection details are printed.

## Expected status codes

| Route | Success | Failure |
|-------|---------|---------|
| `/` | 200 | non-200 or timeout |
| `/account/login` | 200 | non-200 or timeout |
| `/health/live` | 200 | non-200 or timeout |
| `/health/ready` (optional) | 200 | 503 or timeout |

## Failure evidence to collect

Without exposing secrets, record:

- Harness exit code
- Which mandatory probe failed (`[FAIL] Home`, etc.)
- Whether `ready PASS` or `ready FAIL` (if `-TestReadiness` used)
- Windows Event Viewer / console stderr from the launched process (redact credentials before sharing)
- Published artifact path and `dotnet --info` output from the Windows host

## Cleanup

The harness stops the `WorkshopOS.Web.exe` process it started. If validation is interrupted manually:

```powershell
Get-Process WorkshopOS.Web -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Acceptance criteria

**Native Windows execution verified** when, on a trusted Windows host:

1. Mandatory harness probes pass (exit code `0`)
2. Optional readiness passes when PostgreSQL is deliberately configured
3. No startup migration/seed occurs (application does not auto-migrate)
4. Evidence is recorded in release engineering notes

Until then, status remains **NOT YET NATIVE VERIFIED**.

## Related documentation

- [Windows deployment](../deployment/windows.md)
- [CI validation plan](ci-validation-plan.md)
- [Source package manifest](source-package-manifest.md)
