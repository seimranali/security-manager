<#
.SYNOPSIS
    Manage the local Security Manager deployment.
.EXAMPLE
    .\SecurityManager.ps1 status
    .\SecurityManager.ps1 start | stop | restart
.NOTES
    If the Windows services are installed (Install-Services.ps1), this script controls them;
    otherwise it runs the services as background processes for the current user.
#>
param(
    [ValidateSet('status', 'start', 'stop', 'restart')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$config = Import-PowerShellDataFile (Join-Path $root 'services.psd1')
$logs = Join-Path $root 'logs'
$pidFile = Join-Path $root 'run\pids.json'
New-Item -ItemType Directory -Force -Path $logs, (Join-Path $root 'run') | Out-Null

function Get-ServiceName($svc) { "SecurityManager.$($svc.Name)" }
function Test-WindowsService($svc) { [bool](Get-Service -Name (Get-ServiceName $svc) -ErrorAction SilentlyContinue) }

function Test-Health($svc) {
    # Components without an HTTP endpoint (the agent) are healthy while their process/service runs.
    if (-not $svc.Port) {
        if (Test-WindowsService $svc) { return (Get-Service (Get-ServiceName $svc)).Status -eq 'Running' }
        return [bool](Get-RunningProcess $svc)
    }
    try {
        $r = Invoke-WebRequest "http://localhost:$($svc.Port)/health" -UseBasicParsing -TimeoutSec 3
        return $r.StatusCode -eq 200
    } catch {
        # The gateway has no /health of its own; it is healthy if it serves the UI.
        if ($svc.Name -eq 'Gateway') {
            try { return (Invoke-WebRequest "http://localhost:$($svc.Port)/" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { return $false }
        }
        return $false
    }
}

function Get-ExePath($svc) { Join-Path $root "app\$($svc.Name)\$($svc.Exe)" }

# Processes are identified by their executable path, so no PID bookkeeping is needed.
function Get-RunningProcess($svc) {
    $exe = Get-ExePath $svc
    Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($svc.Exe)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe } | Select-Object -First 1
}

# Starts a fully detached, hidden process via WMI: it inherits no handles from this shell and keeps
# running after the shell (or the deploy script) exits.
function Start-Detached($svc) {
    $dir = Join-Path $root "app\$($svc.Name)"
    $log = Join-Path $logs "$($svc.Name).log"
    $err = Join-Path $logs "$($svc.Name).error.log"
    foreach ($f in $log, $err) { if (Test-Path $f) { Move-Item $f "$f.previous" -Force } }

    $command = "cmd.exe /d /c `"`"$(Get-ExePath $svc)`" --environment Production 1>`"$log`" 2>`"$err`"`""
    $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = $command; CurrentDirectory = $dir; ProcessStartupInformation = $startup
    }
    if ($result.ReturnValue -ne 0) { throw "Could not start $($svc.Name) (WMI error $($result.ReturnValue))." }
    return $err
}

function Start-All {
    foreach ($svc in $config.Services) {
        if (Test-WindowsService $svc) {
            try {
                Start-Service (Get-ServiceName $svc) -ErrorAction Stop
                Write-Host ("  {0,-9} Windows service started" -f $svc.Name)
            } catch {
                # Controlling services needs an elevated shell; keep starting the rest.
                Write-Host ("  {0,-9} could not start the Windows service (run as Administrator): Start-Service {1}" -f $svc.Name, (Get-ServiceName $svc)) -ForegroundColor Yellow
            }
            continue
        }
        if (Get-RunningProcess $svc) { Write-Host ("  {0,-9} already running" -f $svc.Name); continue }
        if ($svc.ServiceOnly) {
            Write-Host ("  {0,-9} not installed - run .\Install-Services.ps1 -Components {0} as Administrator" -f $svc.Name) -ForegroundColor Yellow
            continue
        }

        $err = Start-Detached $svc

        # Wait for the service to become healthy before starting dependants.
        $deadline = (Get-Date).AddSeconds(90)
        while (-not (Test-Health $svc) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 700 }
        $proc = Get-RunningProcess $svc
        if ($proc -and (Test-Health $svc)) { Write-Host ("  {0,-9} started  (pid {1}, port {2})" -f $svc.Name, $proc.Id, $svc.Port) -ForegroundColor Green }
        else { Write-Host ("  {0,-9} FAILED to become healthy - see {1}" -f $svc.Name, $err) -ForegroundColor Red }
    }
}

function Stop-All {
    $ordered = @($config.Services)
    [array]::Reverse($ordered)
    foreach ($svc in $ordered) {
        if (Test-WindowsService $svc) {
            Stop-Service (Get-ServiceName $svc) -ErrorAction SilentlyContinue
            Write-Host ("  {0,-9} Windows service stopped" -f $svc.Name)
            continue
        }
        $proc = Get-RunningProcess $svc
        if ($proc) { Stop-Process -Id $proc.Id -Force; Write-Host ("  {0,-9} stopped" -f $svc.Name) }
    }
    if (Test-Path $pidFile) { Remove-Item $pidFile }
}

function Show-Status {
    $rows = foreach ($svc in $config.Services) {
        $mode = 'process'
        $state = 'stopped'
        if (Test-WindowsService $svc) { $mode = 'service'; $state = (Get-Service (Get-ServiceName $svc)).Status.ToString().ToLower() }
        elseif ($p = Get-RunningProcess $svc) { $state = "running (pid $($p.Id))" }
        $healthy = Test-Health $svc
        if ($svc.ServiceOnly -and $mode -eq 'process' -and $state -eq 'stopped') { $state = 'not installed (needs Install-Services.ps1)' }
        [pscustomobject]@{ Service = $svc.Name; Port = $(if ($svc.Port) { $svc.Port } else { '-' }); Mode = $mode; State = $state; Health = $(if ($healthy) { 'healthy' } else { '-' }) }
    }
    $rows | Format-Table -AutoSize
    $info = Join-Path $root 'run\deployment.json'
    if (Test-Path $info) { $d = Get-Content $info -Raw | ConvertFrom-Json; Write-Host "Deployed $($d.DeployedAt) by $($d.DeployedBy) (commit $($d.Commit))" }
    Write-Host 'UI: http://localhost:5000'
}

switch ($Action) {
    'start'   { Start-All }
    'stop'    { Stop-All }
    'restart' { Stop-All; Start-All }
    'status'  { Show-Status }
}
