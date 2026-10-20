# Starts all Security Manager backend services, each in its own window.
# Usage:  .\start-backend.ps1
$root = Join-Path $PSScriptRoot 'backend'

Write-Host 'Building solution...' -ForegroundColor Cyan
dotnet build (Join-Path $root 'SecurityManager.sln') -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error 'Build failed'; exit 1 }

$services = @(
    @{ Name = 'Identity Service :5105'; Path = 'src/Services/Identity/SecurityManager.Identity.API' },
    @{ Name = 'Alert Service    :5104'; Path = 'src/Services/Alert/SecurityManager.Alert.API' },
    @{ Name = 'Security Service :5102'; Path = 'src/Services/Security/SecurityManager.Security.API' },
    @{ Name = 'Firewall Service :5101'; Path = 'src/Services/Firewall/SecurityManager.Firewall.API' },
    @{ Name = 'Scanner Service  :5103'; Path = 'src/Services/Scanner/SecurityManager.Scanner.API' },
    @{ Name = 'Network Service  :5106'; Path = 'src/Services/Network/SecurityManager.Network.API' },
    @{ Name = 'API Gateway      :5000'; Path = 'src/Gateway/SecurityManager.Gateway' }
)

foreach ($s in $services) {
    $project = Join-Path $root $s.Path
    Start-Process powershell -ArgumentList '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = '$($s.Name)'; dotnet run --no-build --launch-profile http --project '$project'"
    Write-Host "Started $($s.Name)" -ForegroundColor Green
}

Write-Host "`nGateway: http://localhost:5000   UI: cd frontend; npm run dev  ->  http://localhost:5173" -ForegroundColor Cyan
