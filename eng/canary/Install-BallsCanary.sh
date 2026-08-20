#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: Install-BallsCanary.sh <package.zip> [checksum] [install-root] [runtime-root] [node-name]" >&2
  exit 2
}

if [[ $# -lt 1 || $# -gt 5 ]]; then
  usage
fi

umask 077
package_path="$(realpath "$1")"
checksum_path="$(realpath "${2:-$package_path.sha256}")"
install_root="$(realpath -m "${3:-${XDG_DATA_HOME:-$HOME/.local/share}/Balls-Canary}")"
runtime_root="$(realpath -m "${4:-${XDG_RUNTIME_DIR:-/tmp/balls-canary-$UID}/Balls-Canary}")"
node_name="${5:-$(hostname)}"
temporary_root="$(mktemp -d)"
daemon_pid=""
pid_path="$install_root/ballsd.pid"

cleanup() {
  if [[ -n "$daemon_pid" ]] && kill -0 "$daemon_pid" 2>/dev/null; then
    kill -TERM "$daemon_pid" 2>/dev/null || true
    wait "$daemon_pid" 2>/dev/null || true
  fi
  rm -rf -- "$temporary_root"
}
trap cleanup ERR INT TERM

checksum_line="$(tr -d '\r\n' <"$checksum_path")"
if [[ ! "$checksum_line" =~ ^([0-9A-Fa-f]{64})\ \ (.+)$ ]]; then
  echo "Invalid archive checksum file: $checksum_path" >&2
  exit 1
fi
if [[ "${BASH_REMATCH[2]}" != "$(basename "$package_path")" ]]; then
  echo "The checksum file names a different archive." >&2
  exit 1
fi
actual_hash="$(sha256sum "$package_path" | cut -d' ' -f1)"
if [[ "${actual_hash,,}" != "${BASH_REMATCH[1],,}" ]]; then
  echo "The Canary archive SHA-256 checksum does not match." >&2
  exit 1
fi

while IFS= read -r entry; do
  if [[ "$entry" == /* || "$entry" == ../* || "$entry" == */../* ]]; then
    echo "The Canary archive contains an unsafe path: $entry" >&2
    exit 1
  fi
done < <(unzip -Z1 "$package_path")

unzip -q "$package_path" -d "$temporary_root/package"
(
  cd "$temporary_root/package"
  sha256sum --check --strict SHA256SUMS
)

manifest="$temporary_root/package/canary.json"
platform="$(sed -n 's/.*"platform": "\([^"]*\)".*/\1/p' "$manifest")"
version="$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' "$manifest")"
commit="$(sed -n 's/.*"commit": "\([^"]*\)".*/\1/p' "$manifest")"
if [[ "$platform" != linux ]] || ! grep -Fq '"runtimeSupported": true' "$manifest"; then
  echo "The selected archive is not a runnable Linux Canary." >&2
  exit 1
fi
if [[ ! "$version" =~ ^[0-9A-Za-z.-]+$ || ! "$commit" =~ ^[0-9a-f]{40}$ ]]; then
  echo "The Canary manifest contains an invalid version or commit identity." >&2
  exit 1
fi

version_id="$version-${commit:0:12}"
versions_root="$install_root/versions"
version_root="$versions_root/$version_id"
state_root="$install_root/state"
socket_path="$runtime_root/control.sock"
mkdir -p "$versions_root" "$state_root" "$runtime_root"
chmod 700 "$install_root" "$versions_root" "$state_root" "$runtime_root"

if [[ -d "$version_root" ]]; then
  installed_commit="$(sed -n 's/.*"commit": "\([^"]*\)".*/\1/p' "$version_root/canary.json")"
  if [[ "$installed_commit" != "$commit" ]]; then
    echo "Install target already contains a different Canary: $version_root" >&2
    exit 1
  fi
else
  mv "$temporary_root/package" "$version_root"
fi
chmod 700 "$version_root/balls/balls" "$version_root/ballsd/ballsd"

if [[ -f "$pid_path" ]]; then
  existing_pid="$(cat "$pid_path")"
  if [[ "$existing_pid" =~ ^[0-9]+$ ]] && kill -0 "$existing_pid" 2>/dev/null; then
    echo "A Balls Canary process is already recorded as PID $existing_pid." >&2
    exit 1
  fi
  rm -f -- "$pid_path"
fi

"$version_root/ballsd/ballsd" \
  --data-directory "$state_root" \
  --pipe-name "$socket_path" \
  --node-name "$node_name" \
  >"$install_root/ballsd.out" \
  2>"$install_root/ballsd.err" &
daemon_pid=$!
printf '%s' "$daemon_pid" >"$pid_path"

ready=false
for _ in $(seq 1 100); do
  if ! kill -0 "$daemon_pid" 2>/dev/null; then
    cat "$install_root/ballsd.err" >&2
    echo "ballsd exited during Canary startup." >&2
    exit 1
  fi
  if "$version_root/balls/balls" --pipe-name "$socket_path" status >"$temporary_root/status.out" 2>/dev/null; then
    cat "$temporary_root/status.out"
    ready=true
    break
  fi
  sleep 0.1
done
if [[ "$ready" != true ]]; then
  echo "Balls Canary did not become ready." >&2
  exit 1
fi

trap - ERR INT TERM
daemon_pid=""
rm -rf -- "$temporary_root"
echo "Installed $version_id in $version_root"
echo "State: $state_root"
echo "Socket: $socket_path"
echo "PID: $(cat "$pid_path")"
