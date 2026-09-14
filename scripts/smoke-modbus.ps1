#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Drive the Modbus TCP link policy end to end against a real client/server pair.

.DESCRIPTION
  Single source of truth for the Modbus smoke: builds the Vion.Examples.ModbusTcp DevHost,
  boots it headless on the REAL clock, and runs the committed scenarios in
  examples/Vion.Examples.ModbusTcp/scenarios through the control API. Exits non-zero unless
  every run reaches `succeeded`, and stops the host it started either way.

  The example's own SimServer and DebugClient are a genuine Modbus TCP client/server pair on
  127.0.0.1:15020, so the scenarios exercise real sockets, real connect failures and the real
  backoff timer - nothing is mocked.

  REAL CLOCK, deliberately. The DevHost's stepped mode drives the SDK's TimeProvider, but the
  TCP client's sockets, connect timeout and operation timeout are real time: under a virtual
  clock a backoff never elapses and every round trip reads zero. So DALE_DEVHOST_STEPPED is
  never set here, and the scenarios' waits are real waits.

  The host is reset before each scenario. The link-policy scenario asserts absolute connect
  counts, which only hold on a freshly booted generation.

  The host picks its own HTTP port - 5000, or the next free one when another host holds it - so the
  script reads the port from the readiness line of the process it started, and stops that process
  rather than whatever listens on a port: that listener may be another session's host.

.PARAMETER LocalSource
  Build the example against the working-tree SDK (-p:DaleLocalSource=true) instead of the
  published Vion.Dale.* packages. This is how an SDK change verifies itself against the
  example BEFORE a release; the default (published packages) is the shape CI builds.

.PARAMETER Scenario
  Run only the named scenario id(s). Default: every committed scenario.

.PARAMETER NoBuild
  Skip the build and boot whatever is already in bin/. Fails loudly if it is not there.

.EXAMPLE
  pwsh scripts/smoke-modbus.ps1
  The release smoke: published packages, every scenario. ~1 minute.

.EXAMPLE
  pwsh scripts/smoke-modbus.ps1 -LocalSource
  The pre-release smoke: the same scenarios against the working-tree SDK.

.EXAMPLE
  pwsh scripts/smoke-modbus.ps1 -Scenario modbus-link-policy -NoBuild
  Re-run one scenario against an already-built example.
#>
[CmdletBinding()]
param(
    [switch]$LocalSource,
    [string[]]$Scenario,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exampleRoot = Join-Path $repoRoot 'examples/Vion.Examples.ModbusTcp'
$devHostDir = Join-Path $exampleRoot 'Vion.Examples.ModbusTcp.DevHost'
$devHostDll = Join-Path $devHostDir 'bin/Debug/net10.0/Vion.Examples.ModbusTcp.DevHost.dll'
$scenarioDir = Join-Path $exampleRoot 'scenarios'

# Set once the host's readiness line names its port.
$baseUri = $null

# The sim server's listener, inside the host process. Unlike the web port it cannot move, so a
# holder is refused by name rather than killed: it may be another session's host.
$simServerPort = 15020

function Assert-SimServerPortFree
{
    $holder = Get-NetTCPConnection -LocalPort $simServerPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($holder)
    {
        $process = Get-Process -Id $holder.OwningProcess -ErrorAction SilentlyContinue
        throw "Port $simServerPort is held by process $( $holder.OwningProcess ) ($( $process.ProcessName )). The example's sim server needs it - stop that process and re-run."
    }
}

# The readiness line is one JSON object on the host's standard output; its port is the port the
# host bound, which is the only port this run may address.
function Wait-ReadinessPort([string]$OutputPath, [System.Diagnostics.Process]$Process, [int]$TimeoutSeconds)
{
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        if ($Process.HasExited)
        {
            throw "The DevHost exited with code $( $Process.ExitCode ) before it was ready. Its output: $OutputPath"
        }

        $line = Get-Content $OutputPath -ErrorAction SilentlyContinue | Where-Object { $_ -match '^\{"ready":true' } | Select-Object -First 1
        if ($line)
        {
            return ($line | ConvertFrom-Json).port
        }

        Start-Sleep -Milliseconds 400
    }

    throw "The DevHost printed no readiness line within $TimeoutSeconds s. Its output: $OutputPath"
}

function Wait-Ready([int]$TimeoutSeconds)
{
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        try
        {
            Invoke-RestMethod "$baseUri/api/control/status" -TimeoutSec 2 | Out-Null
            return $true
        }
        catch
        {
            Start-Sleep -Milliseconds 400
        }
    }

    return $false
}

function Reset-DevHost
{
    Invoke-WebRequest -Method Post -Uri "$baseUri/api/control/reset" -SkipHttpErrorCheck | Out-Null

    # The reset tears the generation down and builds a new one; the port stays bound, so poll
    # for the new generation rather than for the socket.
    Start-Sleep -Seconds 2
    if (-not (Wait-Ready 60))
    {
        throw "The DevHost did not come back after a reset."
    }
}

function Invoke-Scenario([string]$Id)
{
    Reset-DevHost

    $apply = Invoke-WebRequest -Method Post -Uri "$baseUri/api/scenarios/$Id/apply?restart=true" -SkipHttpErrorCheck
    if ($apply.StatusCode -notin @(200, 202))
    {
        throw "Applying '$Id' failed with HTTP $( $apply.StatusCode ): $( $apply.Content )"
    }

    # Recycle-on-run: a 202 carrying `recycling` means the host is switching topology and the
    # caller has to re-apply once it is back (devhost-conventions.md section 8).
    if ($apply.Content -match '"recycling"\s*:\s*true')
    {
        Start-Sleep -Seconds 2
        if (-not (Wait-Ready 60))
        {
            throw "The DevHost did not come back after recycling onto the scenario's topology."
        }

        $apply = Invoke-WebRequest -Method Post -Uri "$baseUri/api/scenarios/$Id/apply?restart=true" -SkipHttpErrorCheck
    }

    # Every wait in these scenarios is a real wait, so the ceiling is generous: the run itself
    # is about 30 s and the point of the ceiling is only to fail rather than hang.
    $deadline = (Get-Date).AddSeconds(300)
    while ((Get-Date) -lt $deadline)
    {
        Start-Sleep -Seconds 2
        $report = Invoke-RestMethod "$baseUri/api/scenarios/$Id/run"
        if ($report.status -notin @('running', 'pending'))
        {
            return $report
        }
    }

    throw "Scenario '$Id' did not finish within 300 s."
}

function Write-Report([string]$Id, $Report)
{
    $seconds = [math]::Round($Report.elapsedSeconds, 1)
    Write-Host ""
    Write-Host "  ${Id}: $( $Report.status ) in ${seconds}s" -ForegroundColor ($Report.status -eq 'succeeded' ? 'Green' : 'Red')

    foreach ($validationError in $Report.validationErrors)
    {
        Write-Host "    validation: $validationError" -ForegroundColor Red
    }

    foreach ($step in @($Report.setup) + @($Report.steps))
    {
        if ($step.status -in @('ok', 'passed'))
        {
            continue
        }

        $label = if ($step.label)
        {
            $step.label
        }
        else
        {
            $step.target
        }
        Write-Host "    [$( $step.status )] $label - $( $step.detail )" -ForegroundColor Red
    }
}

if (-not (Test-Path $scenarioDir))
{
    throw "No scenarios directory at $scenarioDir."
}

$ids = if ($Scenario)
{
    $Scenario
}
else
{
    Get-ChildItem $scenarioDir -Filter '*.scenario.json' | ForEach-Object { $_.Name -replace '\.scenario\.json$', '' } | Sort-Object
}

if (-not $ids)
{
    throw "No scenarios to run."
}

$source = $LocalSource ? 'working-tree SDK (-p:DaleLocalSource=true)' : 'published Vion.Dale.* packages'
Write-Host "Modbus smoke - $source, real clock" -ForegroundColor Cyan
Write-Host "Scenarios: $( $ids -join ', ' )"

# A leftover host from a previous run no longer answers for this one - this run addresses only the
# port its own host printed - but a held sim server port would still fail every connect.
Assert-SimServerPortFree

if (-not $NoBuild)
{
    $buildArgs = @($devHostDir, '--nologo', '-v', 'quiet')
    if ($LocalSource)
    {
        $buildArgs += '-p:DaleLocalSource=true'
    }

    Write-Host "Building the example DevHost..."
    dotnet build @buildArgs
    if ($LASTEXITCODE -ne 0)
    {
        throw "Building the example DevHost failed."
    }
}

if (-not (Test-Path $devHostDll))
{
    throw "No DevHost build at $devHostDll. Drop -NoBuild."
}

$failures = @()
$hostProcess = $null
$hostOutput = Join-Path ([System.IO.Path]::GetTempPath()) "smoke-modbus-$PID.out"
Remove-Item $hostOutput -ErrorAction SilentlyContinue
try
{
    # Folder-driven discovery of topologies/ and scenarios/ is cwd-relative, so the working
    # directory - not the dll's location - is what makes the committed scenarios visible.
    $env:DALE_DEVHOST_NO_BROWSER = '1'
    Remove-Item Env:\DALE_DEVHOST_STEPPED -ErrorAction SilentlyContinue

    Write-Host "Booting the DevHost (real clock)..."
    $hostProcess = Start-Process dotnet -ArgumentList $devHostDll -WorkingDirectory $devHostDir -NoNewWindow -PassThru -RedirectStandardOutput $hostOutput

    $baseUri = "http://localhost:$( Wait-ReadinessPort $hostOutput $hostProcess 90 )"
    if (-not (Wait-Ready 30))
    {
        throw "The DevHost did not answer on $baseUri within 30 s of its readiness line."
    }

    Write-Host "The DevHost is serving on $baseUri."

    $status = Invoke-RestMethod "$baseUri/api/control/status"
    if ($status.stepped)
    {
        throw "The DevHost booted stepped. A virtual clock never lets a connect backoff elapse - unset DALE_DEVHOST_STEPPED."
    }

    foreach ($id in $ids)
    {
        Write-Host ""
        Write-Host "Running $id..." -ForegroundColor Cyan
        $report = Invoke-Scenario $id
        Write-Report $id $report
        if ($report.status -ne 'succeeded')
        {
            $failures += $id
        }
    }
}
finally
{
    if ($hostProcess -and -not $hostProcess.HasExited)
    {
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
        $hostProcess.WaitForExit(10000) | Out-Null
    }
}

Write-Host ""
if ($failures)
{
    Write-Host "Modbus smoke FAILED: $( $failures -join ', ' )" -ForegroundColor Red
    exit 1
}

Write-Host "Modbus smoke passed ($( $ids.Count ) scenario(s))." -ForegroundColor Green
