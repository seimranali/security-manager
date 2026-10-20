SECURITY MANAGER - LINUX DEPLOYMENT
===================================

Requirements
  - 64-bit Linux with systemd (Ubuntu 22.04+, Debian 12+, RHEL/Rocky/Alma 9+, Fedora)
  - ASP.NET Core 10 runtime   Ubuntu/Debian: sudo apt-get install -y aspnetcore-runtime-10.0
                              RHEL/Fedora:   sudo dnf install -y aspnetcore-runtime-10.0
  - nftables (for the Agent)  sudo apt-get install -y nftables   /  sudo dnf install -y nftables
  - No database server: data is kept in SQLite files under /var/lib/security-manager/<service>/.
    Back up that folder (stop the services first, or use: sqlite3 <file> ".backup <copy>").

Install (in the extracted package folder)
  sudo ./install.sh

  Open http://localhost:5000 on the server. Change the default passwords after the first sign-in.
  Re-running install.sh from a newer package upgrades in place and keeps settings + signing key.

Manage
  securitymanager status            health of every component
  securitymanager start|stop|restart
  securitymanager logs Firewall     follow a component's log (journald)

Layout
  /opt/security-manager/app/<Component>/   binaries + appsettings.Production.json (root:securitymanager 640)
  /etc/systemd/system/securitymanager-*.service
  Services run as the unprivileged "securitymanager" user with systemd sandboxing;
  the Agent runs as root (CAP_NET_ADMIN) because it manages nftables.

Host enforcement on Linux (nftables)
  The Agent owns one table, "inet security_manager", and never edits other tables (ufw, firewalld, Docker).
    nft list table inet security_manager      see exactly what is enforced
  - Block rules always take effect (a drop in any nftables table is final).
  - Allow rules cannot open a port that ufw/firewalld drops: nftables evaluates every table.
    Open the port there too, or the Host Enforcement page shows a warning.
  - The host's default is NOT changed to "deny", so enabling enforcement cannot lock you out of SSH.
  Preview without changes:  sudo /opt/security-manager/app/Agent/SecurityManager.Agent --check

Hybrid (Windows + Linux)
  One central server (Windows or Linux) runs the web services; enforcement agents run on every host.
  1. On the central server, let agents reach the Firewall service: in app/Firewall/appsettings.Production.json
     set "Urls": "http://0.0.0.0:5101", restart, and allow TCP 5101 only from the agent hosts.
  2. Copy the central server's Jwt:SigningKey (any app/*/appsettings.Production.json).
  3. Linux agent host:
       sudo ./install.sh --components Agent --firewall-url http://<central>:5101/ --signing-key '<key>'
     Windows agent host (PowerShell as Administrator, in C:\Security Manager):
       .\Install-Services.ps1 -Components Agent
       then set Agent:FirewallServiceUrl and Jwt:SigningKey in app\Agent\appsettings.Production.json
  Every host appears on Firewall > Host Enforcement with its OS and status.
  Note: traffic to port 5101 is plain HTTP; use it on a trusted network or put TLS in front.

Expose the UI to other machines
  Services listen on localhost only. Put nginx (or another reverse proxy) with a real TLS certificate in front:
    location / { proxy_pass http://127.0.0.1:5000; proxy_http_version 1.1;
                 proxy_set_header Upgrade $http_upgrade; proxy_set_header Connection "upgrade";
                 proxy_set_header Host $host; proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                 proxy_set_header X-Forwarded-Proto $scheme; }
  (The Upgrade headers keep the live alert stream - SignalR WebSockets - working.)

Uninstall
  sudo /opt/security-manager/uninstall.sh            remove services + the nftables table (keeps files and DBs)
  sudo /opt/security-manager/uninstall.sh --purge    also delete /opt/security-manager and the service user
