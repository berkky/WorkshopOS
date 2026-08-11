#Requires -Version 5.1
<#
.SYNOPSIS
    Native Windows smoke harness for a published WorkshopOS framework-dependent output.

.DESCRIPTION
    Launches WorkshopOS.Web.exe from a published directory, probes public/liveness endpoints,
    and optionally readiness when the operator has configured database connectivity.

    Does NOT migrate, seed, discover User Secrets, or kill unrelated processes.

.PARAMETER PublishPath
    Path to the published Windows output directory containing WorkshopOS.Web.exe.

.PARAMETER Port
    Local HTTP port for Kestrel. Default: 5928.

.PARAMETER StartupTimeoutSeconds
    Maximum seconds to wait for the process to accept HTTP connections. Default: 30.

.PARAMETER TestReadiness
    When set, also probes /health/ready. Requires ConnectionStrings__WorkshopOS (or
    ASPNETCORE_* equivalent) to be configured by the operator before invocation.

.EXAMPLE
    $env:ConnectionStrings__WorkshopOS = '<from secret manager>'
    .\Test-WorkshopOS-Windows.ps1 -PublishPath C:\publish\workshopos -TestReadiness
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishPath,

    [int] $Port = 5928,

    [int] $StartupTimeoutSeconds = 30,

    [switch] $TestReadiness
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-ProbeResult {
    param(
        [string] $Name,
        [bool] $Passed
    )

    if ($Passed) {
        Write-Host "[PASS] $Name"
    }
    else {
        Write-Host "[FAIL] $Name"
    }
}

$resolvedPublishPath = (Resolve-Path -LiteralPath $PublishPath).Path
$executablePath = Join-Path $resolvedPublishPath 'WorkshopOS.Web.exe'

if (-not (Test-Path -LiteralPath $executablePath)) {
    Write-Error "WorkshopOS.Web.exe was not found under: $resolvedPublishPath"
}

$urls = "http://127.0.0.1:$Port"
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = $executablePath
$processInfo.WorkingDirectory = $resolvedPublishPath
$processInfo.UseShellExecute = $false
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.CreateNoWindow = $true
$processInfo.ArgumentList.Add('--urls')
$processInfo.ArgumentList.Add($urls)

if (-not $env:ASPNETCORE_ENVIRONMENT) {
    $processInfo.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
}

$appProcess = New-Object System.Diagnostics.Process
$appProcess.StartInfo = $processInfo

if (-not $appProcess.Start()) {
    Write-Error 'Failed to start WorkshopOS.Web.exe.'
}

$startedProcessId = $appProcess.Id
$mandatoryPassed = $true
$readinessPassed = $null

try {
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $ready = $false

    while ((Get-Date) -lt $deadline) {
        try {
            $null = Invoke-WebRequest -Uri "$urls/health/live" -UseBasicParsing -TimeoutSec 2
            $ready = $true
            break
        }
        catch {
            if ($appProcess.HasExited) {
                Write-Error 'WorkshopOS.Web.exe exited before becoming reachable.'
            }

            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $ready) {
        Write-Error "Timed out waiting for $urls/health/live."
    }

    $mandatoryRoutes = @(
        @{ Path = '/'; Name = 'Home' },
        @{ Path = '/account/login'; Name = 'Login' },
        @{ Path = '/health/live'; Name = 'Liveness' }
    )

    foreach ($route in $mandatoryRoutes) {
        $uri = "$urls$($route.Path)"
        $passed = $false

        try {
            $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 10
            $passed = ($response.StatusCode -eq 200)
        }
        catch {
            $passed = $false
        }

        Write-ProbeResult -Name $route.Name -Passed $passed
        if (-not $passed) {
            $mandatoryPassed = $false
        }
    }

    if ($TestReadiness) {
        $readinessPassed = $false

        try {
            $response = Invoke-WebRequest -Uri "$urls/health/ready" -UseBasicParsing -TimeoutSec 15
            $readinessPassed = ($response.StatusCode -eq 200)
        }
        catch {
            $readinessPassed = $false
        }

        if ($readinessPassed) {
            Write-Host 'ready PASS'
        }
        else {
            Write-Host 'ready FAIL'
        }
    }
}
finally {
    if (-not $appProcess.HasExited) {
        Stop-Process -Id $startedProcessId -Force -ErrorAction SilentlyContinue
    }

    $appProcess.Dispose()
}

if (-not $mandatoryPassed) {
    exit 1
}

if ($TestReadiness -and -not $readinessPassed) {
    exit 1
}

exit 0
