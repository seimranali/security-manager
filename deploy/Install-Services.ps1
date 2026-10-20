<#
.SYNOPSIS
    Installs Security Manager components as Windows services (run from an elevated PowerShell).
.DESCRIPTION
    - Web services run under their own virtual account (NT SERVICE\SecurityManager.<Name>) that can read and write
      ONLY its own SQLite data folder (data\<name>): least privilege, no passwords.
    - The Firewall service runs as LocalSystem: it applies the firewall rules and IP lists to this PC's Windows Firewall
      itself (built-in enforcement), which needs administrator rights. No separate agent is needed on this PC.
    - The Agent is optional and only installed when asked for (-Components Agent), for other machines.
    - Automatic (delayed) start, restart on failure, logs to the Windows Event Log.
.EXAMPLE
    .\Install-Services.ps1                      # all web services (rules are enforced on this PC)
    .\Install-Services.ps1 -Components Agent    # optional agent, for enforcing on another machine
#>
#Requires -RunAsAdministrator
param(
    [string[]]$Components = @()
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$config = Import-PowerShellDataFile (Join-Path $root 'services.psd1')
# The agent is only for other machines: the Firewall service enforces on this one.
$selected = @($config.Services | Where-Object { ($Components.Count -eq 0 -and $_.Name -ne 'Agent') -or $Components -contains $_.Name })
if ($selected.Count -eq 0) { throw "No matching components. Valid names: $(($config.Services | ForEach-Object Name) -join ', ')" }

# Built-in enforcement in the Firewall service replaces an agent on this PC: remove it so they do not both manage the rules.
if ($Components.Count -eq 0 -and (Get-Service SecurityManager.Agent -ErrorAction SilentlyContinue)) {
    Stop-Service SecurityManager.Agent -ErrorAction SilentlyContinue
    sc.exe delete SecurityManager.Agent | Out-Null
    Write-Host 'Removed the SecurityManager.Agent service (the Firewall service now enforces rules on this PC).' -ForegroundColor Yellow
}

# Stop user-mode processes of the selected components, if any.
foreach ($svc in $selected) {
    Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($svc.Exe)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $root "app\$($svc.Name)\$($svc.Exe)") } | Stop-Process -Force
}

foreach ($svc in $selected) {
    $name = "SecurityManager.$($svc.Name)"
    $dir = Join-Path $root "app\$($svc.Name)"
    $exe = Join-Path $dir $svc.Exe
    # Changing Windows Firewall needs administrator rights: the Firewall service (built-in enforcement) and the agent run as LocalSystem.
    $runsAsSystem = $svc.Name -in 'Agent', 'Firewall'
    $account = if ($runsAsSystem) { 'LocalSystem' } else { "NT SERVICE\$name" }

    if (Get-Service $name -ErrorAction SilentlyContinue) {
        Write-Host "$name already installed; updating configuration."
    } else {
        $description = if ($svc.Name -eq 'Firewall') { "Security Manager Firewall (http://localhost:$($svc.Port)); applies rules to Windows Firewall" }
                       elseif ($svc.Port) { "Security Manager $($svc.Name) (http://localhost:$($svc.Port))" }
                       else { 'Security Manager agent: applies firewall policies and IP lists to Windows Firewall' }
        New-Service -Name $name -BinaryPathName "`"$exe`"" -DisplayName "Security Manager - $($svc.Name)" -Description $description -StartupType Automatic | Out-Null
    }

    sc.exe config $name obj= $account start= delayed-auto | Out-Null
    sc.exe failure $name reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

    # Run in Production mode.
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
    New-ItemProperty -Path $key -Name Environment -PropertyType MultiString -Value @('ASPNETCORE_ENVIRONMENT=Production', 'DOTNET_ENVIRONMENT=Production') -Force | Out-Null

    if (-not $runsAsSystem) {
        # The virtual account needs to read its program folder (which is otherwise restricted).
        icacls $dir /grant "${account}:(OI)(CI)RX" /T /Q | Out-Null
        icacls (Join-Path $root 'app') /grant "${account}:RX" /Q | Out-Null
    }

    if ($svc.Database -and -not $runsAsSystem) {
        # SQLite: the service account may read and write only its own data folder (database, -wal and -shm files).
        $data = Join-Path $root "data\$($svc.Name.ToLowerInvariant())"
        New-Item -ItemType Directory -Force -Path $data | Out-Null
        icacls (Join-Path $root 'data') /grant "${account}:RX" /Q | Out-Null
        icacls $data /grant "${account}:(OI)(CI)M" /T /Q | Out-Null
    }
    Write-Host "Installed $name (runs as $account)" -ForegroundColor Green
    Start-Service $name
}

& (Join-Path $root 'SecurityManager.ps1') status
