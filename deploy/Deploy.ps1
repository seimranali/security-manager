<#
.SYNOPSIS
    Builds and deploys Security Manager to a local folder (default C:\Security Manager).
.DESCRIPTION
    - Publishes all backend services (Release, framework-dependent) and the React UI.
    - The UI is served by the API gateway: open http://localhost:5000.
    - On first deploy, generates a random JWT signing key into appsettings.Production.json files that live only in
      the deployment folder. Re-deploys keep the existing key and settings.
    - Stops the running deployment before copying and starts it again afterwards.
.EXAMPLE
    .\deploy\Deploy.ps1
    .\deploy\Deploy.ps1 -Target 'D:\Apps\Security Manager' -NoStart
#>
[CmdletBinding()]
param(
    [string]$Target = 'C:\Security Manager',
    [switch]$NoStart,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$config = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'services.psd1')
$appRoot = Join-Path $Target 'app'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function New-SigningKey {
    $bytes = New-Object byte[] 64
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

$dataDir = Join-Path $Target 'data'

# SQLite database files: <Target>\data\<service>\<service>.db (one folder per service, so each service account
# can be given access to its own data only).
function New-DatabaseSettings([string]$Service, [switch]$ImportExisting) {
    $db = [ordered]@{ Provider = 'Sqlite'; DataDirectory = (Join-Path $dataDir $Service.ToLowerInvariant()) }
    # One-time copy of the previous SQL Server databases (read-only; skipped once the SQLite file has data).
    if ($ImportExisting -and (Get-Service MSSQLSERVER -ErrorAction SilentlyContinue)) {
        $db.ImportFromSqlServer = 'Server=localhost;Trusted_Connection=True;TrustServerCertificate=True'
    }
    return [pscustomobject]$db
}

function Write-Json($path, $object) {
    $json = $object | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding($false)))
}

# ------------------------------------------------------------------ prerequisites
Step 'Checking prerequisites'
foreach ($tool in 'dotnet', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH." }
}
New-Item -ItemType Directory -Force -Path $Target, $appRoot, (Join-Path $Target 'logs'), (Join-Path $Target 'run'), (Join-Path $Target 'data') | Out-Null

# ------------------------------------------------------------------ tests
if (-not $SkipTests) {
    Step 'Running backend tests'
    dotnet test (Join-Path $repo 'backend\tests\SecurityManager.UnitTests') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed; deployment aborted.' }

    Step 'Running frontend tests'
    Push-Location (Join-Path $repo 'frontend')
    $ErrorActionPreference = 'Continue'
    try { npm run test --silent; if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed; deployment aborted.' } } finally { Pop-Location; $ErrorActionPreference = 'Stop' }
}

# ------------------------------------------------------------------ build UI
Step 'Building the web UI'
Push-Location (Join-Path $repo 'frontend')
# npm/vite print warnings on stderr; Windows PowerShell would turn those into terminating errors under 'Stop'.
$ErrorActionPreference = 'Continue'
try {
    if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund }
    npm run build --silent
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location; $ErrorActionPreference = 'Stop' }

# ------------------------------------------------------------------ stop running deployment
$manage = Join-Path $Target 'SecurityManager.ps1'
if (Test-Path $manage) {
    Step 'Stopping the running deployment'
    & $manage stop
}

# ------------------------------------------------------------------ publish services
$signingKey = $null
$identitySettings = Join-Path $appRoot 'Identity\appsettings.Production.json'
if (Test-Path $identitySettings) {
    $signingKey = (Get-Content $identitySettings -Raw | ConvertFrom-Json).Jwt.SigningKey
}
if (-not $signingKey) {
    $signingKey = New-SigningKey
    Write-Host '    Generated a new JWT signing key for this deployment.' -ForegroundColor Yellow
}

foreach ($svc in $config.Services) {
    Step "Publishing $($svc.Name) service"
    $out = Join-Path $appRoot $svc.Name
    $prodSettings = Join-Path $out 'appsettings.Production.json'
    $kept = $null
    if (Test-Path $prodSettings) { $kept = Get-Content $prodSettings -Raw }

    dotnet publish (Join-Path $repo $svc.Project) -c Release -o $out --nologo -v q -p:UseAppHost=true
    if ($LASTEXITCODE -ne 0) { throw "Publishing $($svc.Name) failed." }

    # Development-only files are not needed in the deployment.
    Remove-Item (Join-Path $out 'appsettings.Development.json') -ErrorAction SilentlyContinue

    if ($kept) {
        # Upgrades from the SQL Server era: switch to SQLite in the shared data folder, importing the old data once.
        if ($svc.Database) {
            $obj = $kept | ConvertFrom-Json
            if (-not $obj.PSObject.Properties['Database']) {
                $obj | Add-Member -NotePropertyName Database -NotePropertyValue (New-DatabaseSettings $svc.Name -ImportExisting)
                $kept = $obj | ConvertTo-Json -Depth 10
                Write-Host "    $($svc.Name): switched to SQLite ($dataDir); existing SQL Server data is imported on first start." -ForegroundColor Yellow
            }
        }
        [IO.File]::WriteAllText($prodSettings, $kept, (New-Object Text.UTF8Encoding($false)))
    } else {
        $settings = [ordered]@{
            Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning'; 'Microsoft.EntityFrameworkCore' = 'Warning'; 'System.Net.Http.HttpClient' = 'Warning'; Yarp = 'Warning' } }
        }
        if ($svc.Port) {
            $settings.Urls = "http://localhost:$($svc.Port)"   # loopback only: not reachable from the network
            $settings.Cors = @{ Origins = @('http://localhost:5000') }
        }
        if ($svc.Name -ne 'Gateway') { $settings.Jwt = @{ SigningKey = $signingKey } }
        if ($svc.Database) { $settings.Database = New-DatabaseSettings $svc.Name }
        Write-Json $prodSettings $settings
    }
}

Step 'Installing the web UI into the gateway'
$wwwroot = Join-Path $appRoot 'Gateway\wwwroot'
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item (Join-Path $repo 'frontend\dist') $wwwroot -Recurse

# ------------------------------------------------------------------ management scripts
Step 'Installing management scripts'
foreach ($file in 'SecurityManager.ps1', 'Install-Services.ps1', 'Uninstall-Services.ps1', 'services.psd1', 'README-DEPLOY.txt') {
    Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $Target $file) -Force
}

# Restrict the folders holding secrets (signing key; databases with password hashes) to Administrators, SYSTEM and
# the current user. Install-Services.ps1 grants each service account what it needs on top.
foreach ($secretDir in $appRoot, $dataDir) {
    try {
        # Access rules only: reading/writing the audit (SACL) section would need SeSecurityPrivilege.
        $dirInfo = Get-Item $secretDir
        $acl = $dirInfo.GetAccessControl('Access')
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($id in 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM', "$env:USERDOMAIN\$env:USERNAME") {
            $acl.SetAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
        }
        $dirInfo.SetAccessControl($acl)
    } catch { Write-Warning "Could not tighten permissions on ${secretDir}: $($_.Exception.Message)" }
}

$commit = 'n/a (not a git repository)'
if ((Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $repo '.git'))) {
    $commit = (& git -C $repo rev-parse --short HEAD) 2>$null
}
Write-Json (Join-Path $Target 'run\deployment.json') ([ordered]@{
    DeployedAt = (Get-Date).ToString('o'); DeployedBy = "$env:USERDOMAIN\$env:USERNAME"; Source = $repo; Commit = $commit
})

if (-not $NoStart) {
    Step 'Starting Security Manager'
    & $manage start
}

Write-Host "`nDeployed to $Target" -ForegroundColor Green
Write-Host '  Open:    http://localhost:5000'
Write-Host "  Manage:  & '$manage' status|start|stop|restart"
