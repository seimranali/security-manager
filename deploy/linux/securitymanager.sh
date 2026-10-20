#!/usr/bin/env bash
# Manage Security Manager on Linux (systemd).
#   securitymanager status | start | stop | restart | logs <component>
set -uo pipefail

ROOT="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
ACTION="${1:-status}"
mapfile -t SERVICES < <(grep -vE '^\s*(#|$)' "$ROOT/services.conf")
installed() { [[ -f "/etc/systemd/system/securitymanager-${1,,}.service" ]]; }

health() {
  local name=$1 port=$2 path=/health
  [[ "$port" == "-" ]] && { systemctl is-active --quiet "securitymanager-${name,,}" && echo healthy || echo -; return; }
  [[ "$name" == "Gateway" ]] && path=/
  curl -fs -m 3 "http://localhost:$port$path" >/dev/null 2>&1 && echo healthy || echo -
}

run_all() {
  local verb=$1 list=("${SERVICES[@]}")
  if [[ "$verb" == "stop" ]]; then
    local reversed=(); for ((i=${#list[@]}-1; i>=0; i--)); do reversed+=("${list[i]}"); done; list=("${reversed[@]}")
  fi
  for line in "${list[@]}"; do
    read -r name _ <<<"$line"
    installed "$name" || continue
    sudo systemctl "$verb" "securitymanager-${name,,}" && printf '  %-9s %s\n' "$name" "${verb}ed" || printf '  %-9s FAILED (journalctl -u securitymanager-%s)\n' "$name" "${name,,}"
  done
}

case "$ACTION" in
  status)
    printf '%-9s %-5s %-14s %s\n' SERVICE PORT STATE HEALTH
    for line in "${SERVICES[@]}"; do
      read -r name port _ <<<"$line"
      if installed "$name"; then state=$(systemctl is-active "securitymanager-${name,,}"); else state="not installed"; fi
      printf '%-9s %-5s %-14s %s\n' "$name" "$port" "$state" "$(health "$name" "$port")"
    done
    echo "UI: http://localhost:5000" ;;
  start|stop|restart) run_all "$ACTION" ;;
  logs)
    [[ -n "${2:-}" ]] || { echo "usage: securitymanager logs <component>" >&2; exit 2; }
    journalctl -u "securitymanager-${2,,}" -n 100 -f ;;
  *) echo "usage: securitymanager status|start|stop|restart|logs <component>" >&2; exit 2 ;;
esac
