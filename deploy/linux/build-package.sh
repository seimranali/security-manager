#!/usr/bin/env bash
# Builds a self-contained Linux package of Security Manager (run on Linux or macOS with the .NET SDK and Node.js).
# On Windows use deploy\Build-LinuxPackage.ps1 instead; both produce the same tarball.
#
#   ./deploy/linux/build-package.sh [--rid linux-x64|linux-arm64] [--skip-tests]
#
# Output: dist/security-manager-<rid>.tar.gz  ->  copy to the server, extract, run: sudo ./install.sh
set -euo pipefail

RID=linux-x64
SKIP_TESTS=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) RID="$2"; shift 2 ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
STAGE="$REPO/dist/security-manager-$RID"
step() { printf '\n\033[36m==> %s\033[0m\n' "$*"; }

for tool in dotnet npm tar; do command -v "$tool" >/dev/null || { echo "$tool was not found on PATH." >&2; exit 1; }; done

if [[ $SKIP_TESTS -eq 0 ]]; then
  step "Running backend tests"
  dotnet test "$REPO/backend/tests/SecurityManager.UnitTests" -c Release --nologo -v q
  step "Running frontend tests"
  (cd "$REPO/frontend" && npm run test --silent)
fi

step "Building the web UI"
(cd "$REPO/frontend" && { [[ -d node_modules ]] || npm ci --no-audit --no-fund; } && npm run build --silent)

rm -rf "$STAGE" && mkdir -p "$STAGE/app"
grep -vE '^\s*(#|$)' "$HERE/services.conf" | while read -r name port exe project key; do
  step "Publishing $name ($RID)"
  # Framework-dependent: the server needs the ASP.NET Core 10 runtime (install.sh checks for it).
  dotnet publish "$REPO/$project" -c Release -r "$RID" --self-contained false -o "$STAGE/app/$name" --nologo -v q
  rm -f "$STAGE/app/$name/appsettings.Development.json"
done

cp -r "$REPO/frontend/dist" "$STAGE/app/Gateway/wwwroot"
cp "$HERE"/{install.sh,uninstall.sh,securitymanager.sh,services.conf,README-LINUX.txt} "$STAGE/"
chmod +x "$STAGE"/*.sh

step "Creating archive"
tar -C "$REPO/dist" -czf "$REPO/dist/security-manager-$RID.tar.gz" "security-manager-$RID"
echo -e "\n\033[32mPackage: $REPO/dist/security-manager-$RID.tar.gz\033[0m"
