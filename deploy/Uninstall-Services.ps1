<#
.SYNOPSIS
    Removes the Security Manager Windows services (run from an elevated PowerShell).
    Databases and SQL logins are left in place.
#>
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$config = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'services.psd1')

foreach ($svc in $config.Services) {
    $name = "SecurityManager.$($svc.Name)"
    if (Get-Service $name -ErrorAction SilentlyContinue) {
        Stop-Service $name -Force -ErrorAction SilentlyContinue
        sc.exe delete $name | Out-Null
        Write-Host "Removed $name"
    }
}
Write-Host 'Services removed. Use SecurityManager.ps1 start to run as background processes instead.'
