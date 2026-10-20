<#
.SYNOPSIS
    Builds the Linux package of Security Manager on Windows (cross-publish, no Linux machine needed).
.DESCRIPTION
    Produces dist\security-manager-<rid>.tar.gz. Copy it to the Linux server, then:
        tar -xzf security-manager-linux-x64.tar.gz && cd security-manager-linux-x64
        sudo ./install.sh --sql-server <host> --sql-user <user> --sql-password '<password>'
    deploy/linux/build-package.sh does the same on Linux.
.EXAMPLE
    .\deploy\Build-LinuxPackage.ps1
    .\deploy\Build-LinuxPackage.ps1 -Rid linux-arm64 -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string]$Rid = 'linux-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$linux = Join-Path $PSScriptRoot 'linux'
$distRoot = Join-Path $repo 'dist'
$stage = Join-Path $distRoot "security-manager-$Rid"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

foreach ($tool in 'dotnet', 'npm', 'tar') {
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

$services = Get-Content (Join-Path $linux 'services.conf') | Where-Object { $_ -notmatch '^\s*(#|$)' } | ForEach-Object {
    $f = $_ -split '\s+'
    [pscustomobject]@{ Name = $f[0]; Project = $f[3] }
}
foreach ($svc in $services) {
    Step "Publishing $($svc.Name) ($Rid)"
    $out = Join-Path $stage "app\$($svc.Name)"
    dotnet publish (Join-Path $repo $svc.Project) -c Release -r $Rid --self-contained false -o $out --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Publishing $($svc.Name) failed." }
    Remove-Item (Join-Path $out 'appsettings.Development.json') -ErrorAction SilentlyContinue
}

Copy-Item (Join-Path $repo 'frontend\dist') (Join-Path $stage 'app\Gateway\wwwroot') -Recurse

# Shell scripts must keep LF line endings to run on Linux.
foreach ($file in 'install.sh', 'uninstall.sh', 'securitymanager.sh', 'services.conf', 'README-LINUX.txt') {
    $text = [IO.File]::ReadAllText((Join-Path $linux $file)) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $stage $file), $text, (New-Object Text.UTF8Encoding($false)))
}

Step 'Creating archive'
$archive = Join-Path $distRoot "security-manager-$Rid.tar.gz"
# Windows tar cannot store the executable bit; install.sh sets it (chmod +x) on the binaries and scripts.
tar -C $distRoot -czf $archive "security-manager-$Rid"
if ($LASTEXITCODE -ne 0) { throw 'tar failed.' }

Write-Host "`nPackage: $archive" -ForegroundColor Green
Write-Host "  On the server:  tar -xzf $(Split-Path $archive -Leaf) && cd security-manager-$Rid && sudo bash ./install.sh --help"
