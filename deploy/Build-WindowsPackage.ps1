<#
.SYNOPSIS
    Builds the Windows package of Security Manager (dist\security-manager-win-x64.zip).
.DESCRIPTION
    The zip contains the published services, the web UI and the installer. On the target server (PowerShell as
    Administrator):  Expand-Archive security-manager-win-x64.zip; .\security-manager-win-x64\Install.ps1
    GitHub Actions runs this on every version tag and attaches the zip to the release.
.EXAMPLE
    .\deploy\Build-WindowsPackage.ps1
    .\deploy\Build-WindowsPackage.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$config = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'services.psd1')
$distRoot = Join-Path $repo 'dist'
$stage = Join-Path $distRoot "security-manager-$Rid"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

foreach ($tool in 'dotnet', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH." }
}

if (-not $SkipTests) {
    Step 'Running backend tests'
    dotnet test (Join-Path $repo 'backend\tests\SecurityManager.UnitTests') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    Step 'Running frontend tests'
    Push-Location (Join-Path $repo 'frontend')
    $ErrorActionPreference = 'Continue'
    try { npm run test --silent; if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed.' } } finally { Pop-Location; $ErrorActionPreference = 'Stop' }
}

Step 'Building the web UI'
Push-Location (Join-Path $repo 'frontend')
# npm/vite print warnings on stderr; Windows PowerShell would turn those into terminating errors under 'Stop'.
$ErrorActionPreference = 'Continue'
try {
    if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund }
    npm run build --silent
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location; $ErrorActionPreference = 'Stop' }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'app') | Out-Null

foreach ($svc in $config.Services) {
    Step "Publishing $($svc.Name) ($Rid)"
    $out = Join-Path $stage "app\$($svc.Name)"
    # Framework-dependent: the server needs the ASP.NET Core 10 runtime (Install.ps1 checks and can install it).
    dotnet publish (Join-Path $repo $svc.Project) -c Release -r $Rid --self-contained false -o $out --nologo -v q -p:UseAppHost=true
    if ($LASTEXITCODE -ne 0) { throw "Publishing $($svc.Name) failed." }
    Remove-Item (Join-Path $out 'appsettings.Development.json') -ErrorAction SilentlyContinue
    Get-ChildItem $out -Filter *.pdb | Remove-Item
}

Copy-Item (Join-Path $repo 'frontend\dist') (Join-Path $stage 'app\Gateway\wwwroot') -Recurse

foreach ($file in 'SecurityManager.ps1', 'Install-Services.ps1', 'Uninstall-Services.ps1', 'services.psd1', 'README-DEPLOY.txt') {
    Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $stage $file)
}
Copy-Item (Join-Path $PSScriptRoot 'windows\Install.ps1') (Join-Path $stage 'Install.ps1')

$version = if ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { 'local' }
Set-Content (Join-Path $stage 'VERSION.txt') $version -Encoding ascii

Step 'Creating archive'
$archive = Join-Path $distRoot "security-manager-$Rid.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path $stage -DestinationPath $archive -CompressionLevel Optimal
Write-Host "`nPackage: $archive" -ForegroundColor Green
