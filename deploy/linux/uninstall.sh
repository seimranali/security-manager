#!/usr/bin/env bash
# Removes the Security Manager systemd services. Keeps the databases.
#   sudo ./uninstall.sh [--purge]     --purge also deletes the install folder and the service account
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "run as root" >&2; exit 1; }

ROOT="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
for unit in /etc/systemd/system/securitymanager-*.service; do
  [[ -e "$unit" ]] || continue
  name=$(basename "$unit" .service)
  systemctl disable --now "$name" 2>/dev/null || true
  rm -f "$unit"
  echo "Removed $name"
done
systemctl daemon-reload

# The agent's nftables table would otherwise keep enforcing the last rule set.
if command -v nft >/dev/null && nft list table inet security_manager >/dev/null 2>&1; then
  nft delete table inet security_manager && echo "Removed nftables table inet security_manager"
fi
rm -f /usr/local/bin/securitymanager

if [[ "${1:-}" == "--purge" ]]; then
  rm -rf "$ROOT"
  id securitymanager >/dev/null 2>&1 && userdel securitymanager
  echo "Purged $ROOT"
fi
