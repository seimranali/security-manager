<#
.SYNOPSIS
    Downloads the latest (or a given) Security Manager release for Windows from GitHub and installs it.
.DESCRIPTION
    PowerShell as Administrator:
        gh release download -R seimranali/security-manager -p get.ps1; .\get.ps1          # private repository
        irm https://github.com/seimranali/security-manager/releases/latest/download/get.ps1 | iex   # public repository
    Private repository: sign in with the GitHub CLI (gh auth login) or set $env:GITHUB_TOKEN (token with repo read).
.EXAMPLE
    .\get.ps1 -Version v1.0.1 -Target 'D:\Apps\Security Manager'
#>
[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [string]$Target = 'C:\Security Manager',
    [string]$Repo = 'seimranali/security-manager'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow with the progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as Administrator.' }

$rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$asset = "security-manager-$rid.zip"
$work = Join-Path ([IO.Path]::GetTempPath()) ("sm-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    Write-Host "Downloading $asset ($Version) from github.com/$Repo ..."
    $useGh = (Get-Command gh -ErrorAction SilentlyContinue) -and ($null -eq (& gh auth status 2>&1 | Select-String 'not logged'))
    if ($useGh) {
        $tagArgs = if ($Version -eq 'latest') { @() } else { @($Version) }
        & gh release download @tagArgs -R $Repo -p "$asset*" -D $work
        if ($LASTEXITCODE -ne 0) { throw "gh release download failed (asset $asset)." }
    } elseif ($env:GITHUB_TOKEN) {
        # API download works for private repositories.
        $headers = @{ Authorization = "Bearer $env:GITHUB_TOKEN"; 'User-Agent' = 'SecurityManager-installer' }
        $api = if ($Version -eq 'latest') { "https://api.github.com/repos/$Repo/releases/latest" } else { "https://api.github.com/repos/$Repo/releases/tags/$Version" }
        $release = Invoke-RestMethod $api -Headers $headers
        foreach ($name in $asset, "$asset.sha256") {
            $item = $release.assets | Where-Object name -eq $name
            if (-not $item) { throw "$name not found in release $($release.tag_name)." }
            Invoke-WebRequest $item.url -Headers ($headers + @{ Accept = 'application/octet-stream' }) -OutFile (Join-Path $work $name) -UseBasicParsing
        }
    } else {
        $base = if ($Version -eq 'latest') { "https://github.com/$Repo/releases/latest/download" } else { "https://github.com/$Repo/releases/download/$Version" }
        try { Invoke-WebRequest "$base/$asset" -OutFile (Join-Path $work $asset) -UseBasicParsing }
        catch { throw "Download failed. Private repository? Run 'gh auth login' or set `$env:GITHUB_TOKEN. ($($_.Exception.Message))" }
        try { Invoke-WebRequest "$base/$asset.sha256" -OutFile (Join-Path $work "$asset.sha256") -UseBasicParsing } catch {}
    }

    $sumFile = Join-Path $work "$asset.sha256"
    if (Test-Path $sumFile) {
        $expected = ((Get-Content $sumFile -Raw).Trim() -split '\s+')[0]
        $actual = (Get-FileHash (Join-Path $work $asset) -Algorithm SHA256).Hash
        if ($expected -ne $actual) { throw "Checksum mismatch for $asset." }
        Write-Host '    Checksum verified.'
    }

    Expand-Archive (Join-Path $work $asset) -DestinationPath $work
    $installer = Get-ChildItem $work -Recurse -Filter Install.ps1 | Select-Object -First 1
    if (-not $installer) { throw 'Install.ps1 not found in the package.' }
    Get-ChildItem $installer.Directory.FullName -Recurse -File | Unblock-File
    & $installer.FullName -Target $Target
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
