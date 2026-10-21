<#
.SYNOPSIS
    Installs or upgrades Security Manager on Windows from the extracted release package.
.DESCRIPTION
    Run from the extracted folder in PowerShell as Administrator:
        .\Install.ps1                                    # installs to C:\Security Manager
        .\Install.ps1 -Target 'D:\Apps\Security Manager'
    - Checks for the ASP.NET Core 10 runtime (offers to install it with winget).
    - Copies the services, generates per-installation settings (random JWT signing key, SQLite data folder).
    - Installs Windows services (start with Windows; the Firewall service enforces rules in Windows Firewall).
    Upgrades keep settings, signing key and data.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Target = 'C:\Security Manager',
    # Copy and configure only; do not install/start the Windows services.
    [switch]$NoServices
)

$ErrorActionPreference = 'Stop'
$package = $PSScriptRoot
$config = Import-PowerShellDataFile (Join-Path $package 'services.psd1')
$appRoot = Join-Path $Target 'app'
$dataDir = Join-Path $Target 'data'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Json($path, $object) {
    [IO.File]::WriteAllText($path, ($object | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
}

# ------------------------------------------------------------------ prerequisites
Step 'Checking the ASP.NET Core 10 runtime'
$hasRuntime = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((& dotnet --list-runtimes 2>$null) -match '^Microsoft\.AspNetCore\.App 10\.')
if (-not $hasRuntime) {
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        Write-Host 'Installing the ASP.NET Core 10 Runtime with winget...' -ForegroundColor Yellow
        winget install --id Microsoft.DotNet.AspNetCore.10 --exact --silent --accept-package-agreements --accept-source-agreements
        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
        $hasRuntime = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((& dotnet --list-runtimes 2>$null) -match '^Microsoft\.AspNetCore\.App 10\.')
    }
    if (-not $hasRuntime) {
        throw 'The ASP.NET Core 10 Runtime (Hosting Bundle) is required: https://dotnet.microsoft.com/download/dotnet/10.0 - install it, then run Install.ps1 again.'
    }
}
Write-Host '    ASP.NET Core 10 runtime found.'

New-Item -ItemType Directory -Force -Path $Target, $appRoot, $dataDir, (Join-Path $Target 'logs'), (Join-Path $Target 'run') | Out-Null

# ------------------------------------------------------------------ stop a running installation
$manage = Join-Path $Target 'SecurityManager.ps1'
if (Test-Path $manage) {
    Step 'Stopping the running installation'
    & $manage stop
}

# ------------------------------------------------------------------ copy services + settings
$signingKey = $null
$identitySettings = Join-Path $appRoot 'Identity\appsettings.Production.json'
if (Test-Path $identitySettings) { $signingKey = (Get-Content $identitySettings -Raw | ConvertFrom-Json).Jwt.SigningKey }
if (-not $signingKey) {
    $bytes = New-Object byte[] 64
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $signingKey = [Convert]::ToBase64String($bytes)
    Write-Host '    Generated a new JWT signing key for this installation.' -ForegroundColor Yellow
}

foreach ($svc in $config.Services) {
    $source = Join-Path $package "app\$($svc.Name)"
    if (-not (Test-Path $source)) { continue }
    Step "Installing $($svc.Name)"
    $out = Join-Path $appRoot $svc.Name
    $prodSettings = Join-Path $out 'appsettings.Production.json'
    $kept = if (Test-Path $prodSettings) { Get-Content $prodSettings -Raw } else { $null }

    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Copy-Item $source $out -Recurse

    if ($kept) {
        [IO.File]::WriteAllText($prodSettings, $kept, (New-Object Text.UTF8Encoding($false)))
    } else {
        $settings = [ordered]@{
            Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning'; 'Microsoft.EntityFrameworkCore' = 'Warning'; 'System.Net.Http.HttpClient' = 'Warning'; Yarp = 'Warning' } }
        }
        if ($svc.Port) {
            $settings.Urls = "http://localhost:$($svc.Port)"   # loopback only; put an HTTPS reverse proxy in front to expose it
            $settings.Cors = @{ Origins = @('http://localhost:5000') }
        }
        if ($svc.Name -ne 'Gateway') { $settings.Jwt = @{ SigningKey = $signingKey } }
        if ($svc.Database) { $settings.Database = [ordered]@{ Provider = 'Sqlite'; DataDirectory = (Join-Path $dataDir $svc.Name.ToLowerInvariant()) } }
        Write-Json $prodSettings $settings
    }
}

Step 'Installing management scripts'
foreach ($file in 'SecurityManager.ps1', 'Install-Services.ps1', 'Uninstall-Services.ps1', 'services.psd1', 'README-DEPLOY.txt', 'VERSION.txt') {
    $path = Join-Path $package $file
    if (Test-Path $path) { Copy-Item $path (Join-Path $Target $file) -Force }
}

# Secrets (signing key, databases) readable only by Administrators and SYSTEM; Install-Services.ps1 adds each service account.
foreach ($secretDir in $appRoot, $dataDir) {
    $dirInfo = Get-Item $secretDir
    $acl = $dirInfo.GetAccessControl('Access')
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($id in 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM') {
        $acl.SetAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    $dirInfo.SetAccessControl($acl)
}

$version = if (Test-Path (Join-Path $package 'VERSION.txt')) { (Get-Content (Join-Path $package 'VERSION.txt') -Raw).Trim() } else { 'unknown' }
Write-Json (Join-Path $Target 'run\deployment.json') ([ordered]@{
    DeployedAt = (Get-Date).ToString('o'); DeployedBy = "$env:USERDOMAIN\$env:USERNAME"; Source = 'GitHub release package'; Commit = $version
})

# ------------------------------------------------------------------ Windows services
if ($NoServices) {
    Write-Host "`nInstalled to $Target (services not installed: run .\Install-Services.ps1 there as Administrator)." -ForegroundColor Green
    return
}
Step 'Installing Windows services'
& (Join-Path $Target 'Install-Services.ps1')

Write-Host "`nSecurity Manager $version installed to $Target" -ForegroundColor Green
Write-Host '  Open:    http://localhost:5000   (first sign-in: admin / Admin@12345 - change it immediately)'
Write-Host "  Manage:  & '$manage' status|start|stop|restart"
