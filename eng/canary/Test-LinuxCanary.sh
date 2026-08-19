#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: Test-LinuxCanary.sh <package.zip> <package.zip.sha256>" >&2
  exit 2
fi

package_path="$(realpath "$1")"
checksum_path="$(realpath "$2")"
package_directory="$(dirname "$package_path")"
smoke_root="$(mktemp -d)"
daemon_pid=""

terminate_daemon() {
  if [[ -n "$daemon_pid" ]] && kill -0 "$daemon_pid" 2>/dev/null; then
    kill -TERM "$daemon_pid" 2>/dev/null || true
    for _ in $(seq 1 100); do
      if ! kill -0 "$daemon_pid" 2>/dev/null; then
        wait "$daemon_pid" 2>/dev/null || true
        daemon_pid=""
        return
      fi
      sleep 0.05
    done

    kill -KILL "$daemon_pid" 2>/dev/null || true
    wait "$daemon_pid" 2>/dev/null || true
    daemon_pid=""
  fi
}

cleanup() {
  terminate_daemon
  rm -rf -- "$smoke_root"
}
trap cleanup EXIT

(
  cd "$package_directory"
  sha256sum --check "$(basename "$checksum_path")"
)

unzip -q "$package_path" -d "$smoke_root/package"
(
  cd "$smoke_root/package"
  sha256sum --check SHA256SUMS
)

chmod 700 "$smoke_root/package/balls/balls" "$smoke_root/package/ballsd/ballsd"
export XDG_STATE_HOME="$smoke_root/xdg-state"
export XDG_RUNTIME_DIR="$smoke_root/xdg-runtime"
mkdir -p "$XDG_RUNTIME_DIR"
chmod 700 "$XDG_RUNTIME_DIR"

"$smoke_root/package/ballsd/ballsd" \
  --node-name "Balls Linux Canary Smoke" \
  >"$smoke_root/ballsd.out" \
  2>"$smoke_root/ballsd.err" &
daemon_pid=$!

ready=false
for _ in $(seq 1 100); do
  if ! kill -0 "$daemon_pid" 2>/dev/null; then
    cat "$smoke_root/ballsd.err" >&2
    echo "Linux Canary daemon exited before readiness." >&2
    exit 1
  fi

  if "$smoke_root/package/balls/balls" status >"$smoke_root/status.out" 2>/dev/null; then
    grep -F "Node: Balls Linux Canary Smoke" "$smoke_root/status.out"
    ready=true
    break
  fi

  sleep 0.05
done

if [[ "$ready" != true ]]; then
  cat "$smoke_root/ballsd.err" >&2
  echo "Linux Canary daemon did not become ready." >&2
  exit 1
fi

terminate_daemon
if [[ -e "$XDG_RUNTIME_DIR/balls/control.sock" ]]; then
  echo "Linux Canary daemon did not clean up its Unix-domain socket." >&2
  exit 1
fi

echo "Linux Canary archive smoke passed with fresh XDG state and orderly shutdown."
