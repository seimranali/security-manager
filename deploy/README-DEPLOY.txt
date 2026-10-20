SECURITY MANAGER - LOCAL DEPLOYMENT
===================================

Open:  http://localhost:5000

Manage (PowerShell, in this folder):
  .\SecurityManager.ps1 status      health of every component
  .\SecurityManager.ps1 start       start everything (waits until healthy)
  .\SecurityManager.ps1 stop
  .\SecurityManager.ps1 restart

Run as Windows services (start with Windows, restart on failure):
  Open PowerShell as Administrator, then:
  .\Install-Services.ps1            each component runs under its own virtual account
                                    with access only to its own data folder
  .\Uninstall-Services.ps1          remove the services again

Port blocking on this PC (no agent needed):
  Every enabled rule and IP list entry is applied to this PC's Windows Firewall by the
  Firewall service itself, within ~10 seconds, under the group "Security Manager (managed)".
  Changing Windows Firewall needs administrator rights, so install the services once:
    .\Install-Services.ps1        (PowerShell as Administrator; Firewall runs as LocalSystem)
  The Rules page shows whether enforcement is active. When the services run as plain
  background processes (SecurityManager.ps1 start) rules are saved but NOT enforced.
  Other machines: .\Install-Services.ps1 -Components Agent on that machine (optional).

Layout:
  app\<Component>\                  published binaries + appsettings.Production.json
  app\Gateway\wwwroot\              the web UI (served by the gateway)
  logs\                             logs when running as background processes
                                    (Windows services log to the Event Log)
  run\                              process ids and deployment info
  data\<component>\<component>.db   SQLite databases (no SQL Server needed). Back up the data
                                    folder while the services are stopped.

Ports (all bound to localhost only, not reachable from the network):
  5000 Gateway + UI   5101 Firewall   5102 Security   5103 Scanner
  5104 Alert          5105 Identity   5106 Network

Security notes:
  - app\*\appsettings.Production.json contains this deployment's JWT signing key.
    The app folder is restricted to Administrators, SYSTEM and the deploying user.
  - Change the default account passwords (admin / analyst / viewer) after first sign-in.
  - Served over plain HTTP on localhost. To expose it to other machines, put it behind
    an HTTPS reverse proxy (IIS / nginx) with a real certificate first.

Linux / hybrid (Windows + Linux):
  .\deploy\Build-LinuxPackage.ps1   (from the source repository) builds dist\security-manager-linux-x64.tar.gz
  Copy it to a Linux server, extract, and run: sudo ./install.sh --help
  Linux hosts enforce rules with nftables; see README-LINUX.txt in the package, including how to run
  agents on many Windows and Linux hosts against one central server.

Update to a new version (from the source repository):
  .\deploy\Deploy.ps1               re-uses the existing key and settings
