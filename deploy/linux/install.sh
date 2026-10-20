#!/usr/bin/env bash
# Installs (or upgrades) Security Manager on Linux as systemd services. Run from the extracted package as root.
#
#   sudo ./install.sh
#
# Data is stored in SQLite files under /var/lib/security-manager/<service>/ (no database server needed).
#
# Options:
#   --data-dir DIR             SQLite data folder (default: /var/lib/security-manager)
#   --prefix DIR               install directory (default: /opt/security-manager)
#   --components "A B"         only these components (default: all, e.g. "Agent" for an enforcement-only host)
#   --firewall-url URL         Agent-only hosts: Firewall service URL of the central server (default: local)
#   --signing-key KEY          JWT signing key; must match the central server for Agent-only hosts
#
# Upgrades keep the existing appsettings.Production.json files (data location, signing key).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX=/opt/security-manager
DATA_DIR=/var/lib/security-manager
COMPONENTS=""
FIREWALL_URL="http://localhost:5101/"
SIGNING_KEY=""
SVC_USER=securitymanager

while [[ $# -gt 0 ]]; do
  case "$1" in
    --data-dir) DATA_DIR="$2"; shift 2 ;;
    --prefix) PREFIX="$2"; shift 2 ;;
    --components) COMPONENTS="$2"; shift 2 ;;
    --firewall-url) FIREWALL_URL="$2"; shift 2 ;;
    --signing-key) SIGNING_KEY="$2"; shift 2 ;;
    -h|--help) sed -n '2,18p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

step() { printf '\n\033[36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[33mwarning: %s\033[0m\n' "$*"; }
die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }
# Default install: every web service. The Agent is only for other machines (the Firewall service enforces on this one).
selected() { if [[ -z "$COMPONENTS" ]]; then [[ "$1" != "Agent" ]]; else [[ " $COMPONENTS " == *" $1 "* ]]; fi; }
json_escape() { local s="${1//\\/\\\\}"; s="${s//\"/\\\"}"; printf '%s' "$s"; }

[[ $EUID -eq 0 ]] || die "run as root (sudo ./install.sh ...)"
command -v systemctl >/dev/null || die "systemd is required"

mapfile -t SERVICES < <(grep -vE '^\s*(#|$)' "$HERE/services.conf")
needs_db=0; needs_agent=0
for line in "${SERVICES[@]}"; do
  read -r name port exe project key <<<"$line"
  selected "$name" || continue
  [[ "$key" != "-" ]] && needs_db=1
  [[ "$name" == "Agent" || "$name" == "Firewall" ]] && needs_agent=1
done

# ------------------------------------------------------------------ prerequisites
step "Checking prerequisites"
if ! command -v dotnet >/dev/null || ! dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft.AspNetCore.App 10\.'; then
  die "the ASP.NET Core 10 runtime is required.
  Ubuntu/Debian:  sudo apt-get install -y aspnetcore-runtime-10.0
  RHEL/Fedora:    sudo dnf install -y aspnetcore-runtime-10.0
  Other:          https://dotnet.microsoft.com/download/dotnet/10.0"
fi
if [[ $needs_agent -eq 1 ]] && ! command -v nft >/dev/null; then
  die "the Agent needs nftables: sudo apt-get install -y nftables   (or: sudo dnf install -y nftables)"
fi

if ! id "$SVC_USER" >/dev/null 2>&1; then
  step "Creating system user $SVC_USER"
  useradd --system --no-create-home --home-dir "$PREFIX" --shell /usr/sbin/nologin "$SVC_USER"
fi

# ------------------------------------------------------------------ stop + copy
for line in "${SERVICES[@]}"; do
  read -r name _ <<<"$line"
  selected "$name" || continue
  systemctl stop "securitymanager-${name,,}" 2>/dev/null || true
done

existing_key=$(grep -ho '"SigningKey": *"[^"]*"' "$PREFIX"/app/*/appsettings.Production.json 2>/dev/null | head -1 | sed 's/.*: *"\(.*\)"/\1/' || true)
SIGNING_KEY="${SIGNING_KEY:-$existing_key}"
if [[ -z "$SIGNING_KEY" ]]; then
  SIGNING_KEY=$(head -c 64 /dev/urandom | base64 -w0)
  echo "Generated a new JWT signing key for this deployment."
fi

mkdir -p "$PREFIX/app"
mkdir -p "$DATA_DIR"
chown root:"$SVC_USER" "$DATA_DIR"
chmod 750 "$DATA_DIR"
for line in "${SERVICES[@]}"; do
  read -r name port exe project key <<<"$line"
  selected "$name" || continue
  step "Installing $name"
  dir="$PREFIX/app/$name"
  kept=""
  [[ -f "$dir/appsettings.Production.json" ]] && kept=$(cat "$dir/appsettings.Production.json")
  rm -rf "$dir" && cp -r "$HERE/app/$name" "$dir"
  chmod +x "$dir/$exe"

  if [[ -n "$kept" ]]; then
    printf '%s\n' "$kept" > "$dir/appsettings.Production.json"
  else
    {
      echo '{'
      echo '  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning", "Microsoft.EntityFrameworkCore": "Warning", "System.Net.Http.HttpClient": "Warning", "Yarp": "Warning" } },'
      if [[ "$port" != "-" ]]; then
        # Loopback only: put an HTTPS reverse proxy (nginx) in front to expose the UI.
        echo "  \"Urls\": \"http://localhost:$port\","
        echo '  "Cors": { "Origins": [ "http://localhost:5000" ] },'
      fi
      if [[ "$key" != "-" ]]; then
        echo "  \"Database\": { \"Provider\": \"Sqlite\", \"DataDirectory\": \"$(json_escape "$DATA_DIR/${name,,}")\" },"
      fi
      [[ "$name" == "Agent" ]] && echo "  \"Agent\": { \"FirewallServiceUrl\": \"$(json_escape "$FIREWALL_URL")\" },"
      if [[ "$name" != "Gateway" ]]; then echo "  \"Jwt\": { \"SigningKey\": \"$SIGNING_KEY\" }"; else echo '  "AllowedHosts": "*"'; fi
      echo '}'
    } > "$dir/appsettings.Production.json"
  fi

  # Secrets (connection string, signing key) readable only by root and the service account.
  chown -R root:"$SVC_USER" "$dir"
  chmod -R u=rwX,g=rX,o= "$dir"
  chmod 640 "$dir/appsettings.Production.json"
  if [[ "$key" != "-" ]]; then
    # SQLite database (+ -wal/-shm files) for this service only.
    install -d -o "$SVC_USER" -g "$SVC_USER" -m 700 "$DATA_DIR/${name,,}"
  fi
done
for f in securitymanager.sh uninstall.sh services.conf README-LINUX.txt; do install -m 755 "$HERE/$f" "$PREFIX/$f"; done
chmod 644 "$PREFIX/services.conf" "$PREFIX/README-LINUX.txt"
ln -sf "$PREFIX/securitymanager.sh" /usr/local/bin/securitymanager

# ------------------------------------------------------------------ systemd units
step "Writing systemd units"
for line in "${SERVICES[@]}"; do
  read -r name port exe project key <<<"$line"
  selected "$name" || continue
  unit="securitymanager-${name,,}"
  dir="$PREFIX/app/$name"
  if [[ "$name" == "Agent" ]]; then
    # Managing nftables needs root / CAP_NET_ADMIN.
    account="User=root"
    hardening="CapabilityBoundingSet=CAP_NET_ADMIN CAP_NET_RAW
NoNewPrivileges=yes
ProtectHome=yes
ProtectSystem=full"
    after="network-online.target securitymanager-firewall.service"
    desc="Security Manager agent: applies firewall policies and IP lists to nftables"
  else
    account="User=$SVC_USER
Group=$SVC_USER"
    hardening="NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
# Firewall applies the rules to this host's nftables (built-in enforcement): CAP_NET_ADMIN only, no root.
# Scanner may use raw sockets (ping). Every other service runs with no capabilities at all.
CapabilityBoundingSet=$(case "$name" in Firewall) echo CAP_NET_ADMIN ;; Scanner) echo CAP_NET_RAW ;; esac)
AmbientCapabilities=$(case "$name" in Firewall) echo CAP_NET_ADMIN ;; Scanner) echo CAP_NET_RAW ;; esac)"
    after="network-online.target$([[ "$name" != "Identity" ]] && echo ' securitymanager-identity.service')"
    desc="Security Manager $name (http://localhost:$port)"
  fi
  cat > "/etc/systemd/system/$unit.service" <<EOF
[Unit]
Description=$desc
After=$after
Wants=network-online.target

[Service]
Type=notify
$account
WorkingDirectory=$dir
$([[ "$key" != "-" ]] && printf 'ReadWritePaths=%s' "$DATA_DIR/${name,,}")
ExecStart=$dir/$exe
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1
Restart=on-failure
RestartSec=5
TimeoutStartSec=120
SyslogIdentifier=$unit
$hardening

[Install]
WantedBy=multi-user.target
EOF
done
systemctl daemon-reload

# ------------------------------------------------------------------ start
step "Starting services"
for line in "${SERVICES[@]}"; do
  read -r name port _ <<<"$line"
  selected "$name" || continue
  unit="securitymanager-${name,,}"
  systemctl enable --now "$unit" >/dev/null 2>&1 || warn "$unit failed to start - see: journalctl -u $unit -n 50"
  if [[ "$port" != "-" ]] && command -v curl >/dev/null; then
    path=/health; [[ "$name" == "Gateway" ]] && path=/
    for _ in $(seq 1 60); do curl -fs "http://localhost:$port$path" >/dev/null && break; sleep 1; done
  fi
done

"$PREFIX/securitymanager.sh" status || true
echo -e "\n\033[32mInstalled to $PREFIX\033[0m"
echo "  UI (on this host):  http://localhost:5000      change the default passwords after first sign-in"
echo "  Manage:             securitymanager status|start|stop|restart|logs <component>"
