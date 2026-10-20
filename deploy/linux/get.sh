#!/usr/bin/env bash
# Downloads the latest (or a given) Security Manager release for this machine and installs it.
#
#   curl -fsSL https://github.com/seimranali/security-manager/releases/latest/download/get.sh | sudo bash
#   sudo bash get.sh [--version v1.0.0] [install.sh options...]
#
# Private repository: authenticate first with the GitHub CLI (gh auth login) or export GITHUB_TOKEN=<token with repo read>.
set -euo pipefail

REPO="${SM_REPO:-seimranali/security-manager}"
VERSION=latest
INSTALL_ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    *) INSTALL_ARGS+=("$1"); shift ;;
  esac
done

die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }
[[ $EUID -eq 0 ]] || die "run as root (sudo)"

case "$(uname -m)" in
  x86_64|amd64) RID=linux-x64 ;;
  aarch64|arm64) RID=linux-arm64 ;;
  *) die "unsupported CPU architecture: $(uname -m)" ;;
esac
ASSET="security-manager-$RID.tar.gz"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "Downloading $ASSET ($VERSION) from github.com/$REPO ..."
if command -v gh >/dev/null && gh auth status >/dev/null 2>&1; then
  if [[ "$VERSION" == latest ]]; then gh release download -R "$REPO" -p "$ASSET*" -D "$WORK"
  else gh release download "$VERSION" -R "$REPO" -p "$ASSET*" -D "$WORK"; fi
else
  command -v curl >/dev/null || die "curl is required"
  if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    # API download works for private repositories.
    api="https://api.github.com/repos/$REPO/releases/$([[ $VERSION == latest ]] && echo latest || echo "tags/$VERSION")"
    release=$(curl -fsSL -H "Authorization: Bearer $GITHUB_TOKEN" "$api") || die "cannot read release $VERSION (check GITHUB_TOKEN)"
    for name in "$ASSET" "$ASSET.sha256"; do
      id=$(printf '%s' "$release" | grep -B3 "\"name\": \"$name\"" | grep -o '"id": [0-9]*' | head -1 | grep -o '[0-9]*') || true
      [[ -n "$id" ]] || die "$name not found in release $VERSION"
      curl -fsSL -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/octet-stream" \
        "https://api.github.com/repos/$REPO/releases/assets/$id" -o "$WORK/$name"
    done
  else
    base="https://github.com/$REPO/releases/$([[ $VERSION == latest ]] && echo latest/download || echo "download/$VERSION")"
    curl -fsSL "$base/$ASSET" -o "$WORK/$ASSET" || die "download failed (private repository? set GITHUB_TOKEN or use gh auth login)"
    curl -fsSL "$base/$ASSET.sha256" -o "$WORK/$ASSET.sha256" || true
  fi
fi

if [[ -f "$WORK/$ASSET.sha256" ]]; then
  (cd "$WORK" && sha256sum -c "$ASSET.sha256") || die "checksum mismatch"
fi

tar -xzf "$WORK/$ASSET" -C "$WORK"
cd "$WORK/security-manager-$RID"
bash ./install.sh "${INSTALL_ARGS[@]}"
