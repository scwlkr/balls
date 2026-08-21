#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 inspect|configure|disable [--confirm-system-change]" >&2
  exit 2
}

action="${1:-}"
confirmation="${2:-}"
case "$action" in
  inspect|configure|disable) ;;
  *) usage ;;
esac

tailscale_command="$(command -v tailscale || true)"
if [[ -z "$tailscale_command" && -x /Applications/Tailscale.app/Contents/MacOS/Tailscale ]]; then
  tailscale_command=/Applications/Tailscale.app/Contents/MacOS/Tailscale
fi

inspect() {
  local status_json='{}'
  if [[ -n "$tailscale_command" ]]; then
    status_json="$($tailscale_command status --json 2>/dev/null || echo '{}')"
  fi

  STATUS_JSON="$status_json" /usr/bin/python3 - <<'PY'
import json
import os

try:
    status = json.loads(os.environ["STATUS_JSON"])
except (KeyError, json.JSONDecodeError):
    status = {}

print(json.dumps({
    "computerName": os.uname().nodename,
    "tailscaleState": status.get("BackendState", "NotInstalled"),
    "tailscaleIPs": status.get("TailscaleIPs", []),
    "tailscaleSshEnabled": bool(status.get("Self", {}).get("TailscaleSSHEnabled", False)),
}, separators=(",", ":")))
PY
}

if [[ "$action" == inspect ]]; then
  inspect
  exit 0
fi

if [[ "$confirmation" != --confirm-system-change ]]; then
  echo "This action changes persistent remote-access settings. Re-run with --confirm-system-change after reviewing docs/two-machine-development.md." >&2
  exit 1
fi

if [[ -z "$tailscale_command" ]]; then
  echo "Tailscale is not installed. Install and sign in with the official macOS app first." >&2
  exit 1
fi

if [[ "$action" == configure ]]; then
  open -a Tailscale
  "$tailscale_command" up
  "$tailscale_command" set --ssh=true
  inspect
  exit 0
fi

"$tailscale_command" set --ssh=false
inspect
