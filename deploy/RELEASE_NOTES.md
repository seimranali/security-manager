## Install

### Windows (PowerShell as Administrator)

```powershell
# Private repository: sign in first with  gh auth login  (or set $env:GITHUB_TOKEN)
gh release download -R seimranali/security-manager -p get.ps1 --clobber; .\get.ps1
```

Or download `security-manager-win-x64.zip`, extract it, and run `.\Install.ps1` as Administrator.
Installs to `C:\Security Manager` as Windows services; the ASP.NET Core 10 runtime is installed with winget if missing.

### Linux

```bash
gh release download -R seimranali/security-manager -p get.sh --clobber && sudo bash get.sh
```

Or download `security-manager-linux-x64.tar.gz` (or `-arm64`), extract it, and run `sudo ./install.sh`.
Needs the ASP.NET Core 10 runtime and nftables.

Then open **http://localhost:5000** and sign in as `admin` / `Admin@12345` — change the password immediately.
Upgrading = running the installer of a newer release (settings and data are kept).
