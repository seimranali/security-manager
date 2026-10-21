# Security Manager

Web-based security management platform: **firewall policy engine**, **security event detection (SIEM-style)**, **automated vulnerability scanning** and **real-time alerting**.

| Layer | Tech |
|---|---|
| Frontend | React 19 + TypeScript + Vite, custom CSS design system (no CSS framework), TanStack Query, Recharts, SignalR |
| Backend | ASP.NET Core Web API (.NET 10), 4 microservices + YARP API Gateway |
| Data | MS SQL Server via EF Core 10 — one database per service |

## Architecture

```
React UI (5173) ──► API Gateway / YARP (5000) ──┬─► Firewall Service (5101) ──► SecurityManager_Firewall
                         ▲  WebSocket            ├─► Security Service (5102) ──► SecurityManager_Security
                         │                       ├─► Scanner Service  (5103) ──► SecurityManager_Scanner
                         └──── SignalR hub ◄─────┴─► Alert Service    (5104) ──► SecurityManager_Alert

Firewall ──(denied traffic → event)──► Security ──(new detection → alert)──► Alert ──(push)──► UI
Scanner  ──(critical/high findings → alert)──────────────────────────────────► Alert
```

### 4-layer structure (per service)

```
SecurityManager.<Service>.Domain          Entities, enums, pure domain logic (no dependencies)
SecurityManager.<Service>.Application     DTOs, services/use cases, abstractions (I<Service>UnitOfWork, publishers)
SecurityManager.<Service>.Infrastructure  EF Core DbContext, Unit of Work, HTTP integrations, background workers
SecurityManager.<Service>.API             Controllers, Program.cs, hubs
```

Shared building blocks (`src/BuildingBlocks`):
- **Shared.Kernel** — `BaseEntity`, `IRepository<T>`, `IUnitOfWork`, `PagedResult`, domain exceptions, integration contracts
- **Shared.Infrastructure** — generic EF `Repository<T>` and `UnitOfWork<TContext>` (atomic `SaveChangesAsync`, transactions, audit timestamps)
- **Shared.Web** — problem-details exception handler, OpenAPI/Scalar, CORS, health checks

Each service exposes a typed Unit of Work (e.g. `IFirewallUnitOfWork.Policies / Rules / TrafficLogs`) so a use case's changes commit together.

## Services

- **Firewall** — policies (priority, direction, default action) and rules (CIDR/range/list addresses, port ranges, protocol, direction). Packet evaluation engine (first match wins → policy default → implicit deny), hit counters, traffic log, stats.
- **Security** — event ingestion, threshold correlation **detection rules** (mapped to MITRE ATT&CK), detections with evidence and triage workflow.
- **Scanner** — targets, on-demand and **scheduled automated scans** (background scheduler + worker queue): TCP port scan with banner grab, HTTP security-header audit, TLS certificate checks; findings with risk scoring.
- **Alert** — alert deduplication/escalation, acknowledge/resolve/reopen, bulk actions, MTTA/MTTR stats, real-time SignalR push.

## Network service (gateways, routing, NAT, VPN, HA)

The Network service (port 5106, `SecurityManager_Network`) manages the configuration of **Linux gateways** and generates deployable files. It does not push changes to devices itself: download the bundle, apply it, then click **Mark deployed**. A future agent can automate this step.

| Area | Modelled | Generated as |
|---|---|---|
| Network interfaces | Physical, VLAN (802.1Q), bond, bridge, loopback; zones; static/DHCP; MTU | `network/apply-network.sh` (iproute2) |
| Routing | Static routes, multiple tables, metrics | same script (`ip route`) |
| Dedicated gateway routing | Policy rules by source / destination / input interface → table (e.g. guest via ISP B) | same script (`ip rule`) |
| NAT | Masquerade, source NAT, port forward (ranges), 1:1 NAT | `nftables/sm-nat.nft` |
| VPN | WireGuard interfaces + peers; IPsec IKEv2 site-to-site | `wireguard/<if>.conf`, `strongswan/swanctl.d/*.conf` |
| High availability | Active/passive clusters, priorities, preemption, virtual IPs | `keepalived/keepalived.conf` (VRRP, unicast peers) |

- **Secrets are never stored:** the WireGuard private key and the IPsec pre-shared key stay on the device, and the generated configs read them from local files.
- **Configuration checks:** cover overlapping subnets, unreachable next hops, policy tables with no routes, port-forward and VPN-port conflicts, overlapping peer allowed-IPs, and VIP problems such as outside-subnet, duplicate or missing-interface addresses.
- **Revisions:** every change bumps the gateway's configuration revision; the UI shows pending changes until the revision is marked deployed. Cluster changes bump every member.

## Authentication

A dedicated **Identity Service** (port 5105, `SecurityManager_Identity`) issues tokens; every service validates them.

| Token | Lifetime | Where it lives |
|---|---|---|
| Access token (JWT, HS256) | 15 min | SPA memory only, sent as `Authorization: Bearer` |
| Refresh token (opaque, 512-bit) | 7 days, sliding | `sm_refresh` cookie: HttpOnly, Secure, SameSite=Strict, Path=/api/auth. Only its SHA-256 hash is stored |

- **Rotation:** every `/api/auth/refresh` revokes the presented refresh token and issues a new one.
- **Reuse detection:** presenting an already-rotated token revokes the whole token family (all sessions from that login). A 30 s grace window absorbs harmless races, e.g. two tabs refreshing at once.
- **Lockout:** 5 failed sign-ins lock the account for 15 min. The gateway also rate-limits login/refresh to 10 requests per minute per IP.
- **Password change / role change / disable** revokes the user's refresh tokens.
- **Roles:** `Admin` (users + everything), `Analyst` (read/write), `Viewer` (read-only; non-GET requests return 403).
- **Service-to-service** calls carry a 5-minute JWT with role `Service`.
- **SignalR** passes the access token in the `access_token` query string on `/hubs/*`.
- **Frontend:** silent refresh about 1 min before expiry, a single refresh shared by concurrent requests (and serialized across tabs with Web Locks), retry once on 401, and sign-out broadcast across tabs.

Seeded accounts (change these immediately): `admin / Admin@12345`, `analyst / Analyst@12345`, `viewer / Viewer@12345`.

> **Production:** replace `Jwt:SigningKey` in every service with a secret of at least 32 random bytes from environment variables or a secret store. Serve over HTTPS; `Auth:SecureCookies` must stay `true`.

## Windows installation

Every version tag is also built into `security-manager-win-x64.zip` on the [GitHub Release](../../releases).
Requirements: Windows 10/11 or Windows Server 2019+ (64-bit). The ASP.NET Core 10 runtime is installed automatically
with winget when missing; no SQL Server needed (SQLite).

In **PowerShell as Administrator**:

```powershell
# Private repository: sign in first with  gh auth login  (or set $env:GITHUB_TOKEN = '<token with repo read>')
gh release download -R seimranali/security-manager -p get.ps1 --clobber; .\get.ps1
# Public repository:
irm https://github.com/seimranali/security-manager/releases/latest/download/get.ps1 | iex
```

`get.ps1` downloads the zip, verifies its checksum and runs `Install.ps1`, which installs to `C:\Security Manager`
(`-Target` to change), generates a unique signing key, and registers Windows services that start with Windows. The
Firewall service runs as LocalSystem and applies your rules to Windows Firewall. **Or manually:** extract the zip and run
`.\Install.ps1` as Administrator. Upgrade = run `get.ps1` again (settings and data are kept); uninstall =
`C:\Security Manager\Uninstall-Services.ps1`.

## Linux installation

Every version tag (`v1.0.0`, `v1.0.1`, …) is built by GitHub Actions into ready-to-install packages for
**linux-x64** and **linux-arm64**, attached to the [GitHub Release](../../releases). The server needs no SDKs.

**Requirements on the server:** 64-bit Linux with systemd (Ubuntu 22.04+, Debian 12+, RHEL/Rocky/Alma 9+, Fedora),
the ASP.NET Core 10 runtime and nftables:

```bash
# Ubuntu / Debian (the runtime package may need Microsoft's repository on older releases)
sudo apt-get install -y aspnetcore-runtime-10.0 nftables
# RHEL / Rocky / Alma / Fedora
sudo dnf install -y aspnetcore-runtime-10.0 nftables
```

**Install the latest release** (detects x64/arm64, verifies the checksum, runs the installer):

```bash
# Private repository: sign in first with  gh auth login  (or export GITHUB_TOKEN=<token with repo read access>)
gh release download -R seimranali/security-manager -p get.sh && sudo bash get.sh
# Public repository:
curl -fsSL https://github.com/seimranali/security-manager/releases/latest/download/get.sh | sudo bash
```

**Or manually:** download `security-manager-linux-x64.tar.gz` from the release, then

```bash
tar -xzf security-manager-linux-x64.tar.gz && cd security-manager-linux-x64
sudo ./install.sh
```

Then open `http://localhost:5000` on the server (sign in as `admin` / `Admin@12345` and change the password).
Services run under systemd (`securitymanager status|start|stop|restart|logs <component>`), data is kept in SQLite under
`/var/lib/security-manager`, and firewall rules are enforced through nftables. Upgrading = installing a newer release
(settings and data are kept). Details: [deploy/linux/README-LINUX.txt](deploy/linux/README-LINUX.txt).

**Build from source on Linux** instead (needs the .NET 10 SDK and Node.js 22):

```bash
git clone https://github.com/seimranali/security-manager.git && cd security-manager
bash deploy/linux/build-package.sh          # → dist/security-manager-linux-x64.tar.gz
```

**Publish a new release:** `git tag v1.0.1 && git push origin v1.0.1`.

## Running locally

Prerequisites: .NET 10 SDK, Node 20+, SQL Server (default instance, Windows auth). Connection strings are in each `*.API/appsettings.json`.

```powershell
.\start-backend.ps1          # builds and starts the 4 services + gateway (databases are created & seeded on first run)
cd frontend
npm install
npm run dev                  # http://localhost:5173
```

API docs (development): `http://localhost:510x/scalar` for each service.

Tests: `cd backend; dotnet test`

> ⚠️ Only scan hosts you own or are authorized to test. The seeded `scanme.nmap.org` target is provided by the Nmap project for test scanning.

## Production notes / next steps

- Move JWT signing to asymmetric keys (RS256/ES256) so services hold only the public key.
- Replace `EnsureCreated` with EF Core migrations.
- Replace synchronous HTTP between services with a message broker (RabbitMQ/Azure Service Bus) for guaranteed delivery.
