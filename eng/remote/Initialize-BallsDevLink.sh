#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 inspect|configure|disable [--confirm-system-change] [--key-item <1Password item>]" >&2
  exit 2
}

action="${1:-}"
shift || true
case "$action" in
  inspect|configure|disable) ;;
  *) usage ;;
esac

confirmed=false
key_item='Balls Dev Link'
while [[ $# -gt 0 ]]; do
  case "$1" in
    --confirm-system-change)
      confirmed=true
      shift
      ;;
    --key-item)
      [[ $# -ge 2 ]] || usage
      key_item="$2"
      shift 2
      ;;
    *) usage ;;
  esac
done

label='com.scwlkr.balls-dev-link-sshd'
daemon_config='/etc/ssh/sshd_config_balls_dev_link'
daemon_plist="/Library/LaunchDaemons/$label.plist"
tailscale_command="$(command -v tailscale || true)"
if [[ -z "$tailscale_command" && -x /Applications/Tailscale.app/Contents/MacOS/Tailscale ]]; then
  tailscale_command=/Applications/Tailscale.app/Contents/MacOS/Tailscale
fi

inspect() {
  local status_json='{}'
  local service_loaded=false
  if [[ -n "$tailscale_command" ]]; then
    status_json="$($tailscale_command status --json 2>/dev/null || echo '{}')"
  fi
  if launchctl print "system/$label" >/dev/null 2>&1; then
    service_loaded=true
  fi

  STATUS_JSON="$status_json" SERVICE_LOADED="$service_loaded" DAEMON_CONFIG="$daemon_config" \
    /usr/bin/python3 - <<'PY'
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
    "privateSshLoaded": os.environ["SERVICE_LOADED"] == "true",
    "privateSshConfigured": os.path.isfile(os.environ["DAEMON_CONFIG"]),
}, separators=(",", ":")))
PY
}

run_as_administrator() {
  /usr/bin/osascript - "$1" <<'APPLESCRIPT'
on run arguments
  do shell script (item 1 of arguments) with administrator privileges
end run
APPLESCRIPT
}

if [[ "$action" == inspect ]]; then
  inspect
  exit 0
fi

if [[ "$confirmed" != true ]]; then
  echo "This action changes persistent remote-access settings. Re-run with --confirm-system-change after reviewing docs/two-machine-development.md." >&2
  exit 1
fi

if [[ "$(uname -s)" != Darwin ]]; then
  echo 'This bootstrap must run on macOS.' >&2
  exit 1
fi

if [[ "$action" == disable ]]; then
  run_as_administrator "/bin/launchctl disable system/$label; /bin/launchctl bootout system/$label >/dev/null 2>&1 || true"
  inspect
  exit 0
fi

if [[ -z "$tailscale_command" ]]; then
  echo "Tailscale is not installed. Install and sign in with the official macOS app first." >&2
  exit 1
fi
if ! command -v op >/dev/null 2>&1; then
  echo '1Password CLI is required to read only the selected SSH public key.' >&2
  exit 1
fi

open -a Tailscale
"$tailscale_command" up
tailscale_ip="$($tailscale_command ip -4 | head -n 1)"
if [[ ! "$tailscale_ip" =~ ^100\.([6-9][0-9]|1[01][0-9]|12[0-7])\.[0-9]{1,3}\.[0-9]{1,3}$ ]]; then
  echo 'Tailscale did not provide a private IPv4 address.' >&2
  exit 1
fi

public_key="$(op item get "$key_item" --fields public_key)"
if [[ ! "$public_key" =~ ^ssh-(ed25519|rsa)\ [A-Za-z0-9+/=]+(\ .{1,200})?$ ]]; then
  echo 'The selected 1Password item does not contain one supported SSH public key.' >&2
  exit 1
fi

ssh_directory="$HOME/.ssh"
authorized_keys="$ssh_directory/authorized_keys"
if [[ -L "$ssh_directory" || -L "$authorized_keys" ]]; then
  echo 'Refusing linked SSH authorization paths.' >&2
  exit 1
fi
mkdir -p "$ssh_directory"
chmod 700 "$ssh_directory"
touch "$authorized_keys"
chmod 600 "$authorized_keys"
if ! grep -Fqx -- "$public_key" "$authorized_keys"; then
  printf '%s\n' "$public_key" >> "$authorized_keys"
fi

temporary_directory="$(mktemp -d "${TMPDIR:-/tmp}/balls-dev-link.XXXXXX")"
trap 'rm -rf "$temporary_directory"' EXIT
temporary_config="$temporary_directory/sshd_config"
temporary_plist="$temporary_directory/$label.plist"

printf '%s\n' \
  'Port 22' \
  'AddressFamily inet' \
  "ListenAddress $tailscale_ip" \
  'HostKey /etc/ssh/ssh_host_ed25519_key' \
  'HostKey /etc/ssh/ssh_host_ecdsa_key' \
  'PidFile /var/run/balls-dev-link-sshd.pid' \
  'AuthorizedKeysFile .ssh/authorized_keys' \
  'AuthenticationMethods publickey' \
  'PubkeyAuthentication yes' \
  'PasswordAuthentication no' \
  'KbdInteractiveAuthentication no' \
  'PermitRootLogin no' \
  'StrictModes yes' \
  'UsePAM yes' \
  'UseDNS no' \
  'X11Forwarding no' \
  'PermitTunnel no' \
  'AllowAgentForwarding no' \
  'AllowTcpForwarding yes' \
  "AllowUsers $USER" \
  'Subsystem sftp internal-sftp' > "$temporary_config"

printf '%s\n' \
  '<?xml version="1.0" encoding="UTF-8"?>' \
  '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">' \
  '<plist version="1.0">' \
  '<dict>' \
  '  <key>Label</key>' \
  "  <string>$label</string>" \
  '  <key>ProgramArguments</key>' \
  '  <array>' \
  '    <string>/usr/sbin/sshd</string>' \
  '    <string>-D</string>' \
  '    <string>-e</string>' \
  '    <string>-f</string>' \
  "    <string>$daemon_config</string>" \
  '  </array>' \
  '  <key>KeepAlive</key>' \
  '  <true/>' \
  '  <key>ProcessType</key>' \
  '  <string>Background</string>' \
  '</dict>' \
  '</plist>' > "$temporary_plist"

plutil -lint "$temporary_plist" >/dev/null
root_command="/usr/bin/ssh-keygen -A"
root_command+=" && /usr/bin/install -o root -g wheel -m 600 '$temporary_config' '$daemon_config'"
root_command+=" && /usr/bin/install -o root -g wheel -m 644 '$temporary_plist' '$daemon_plist'"
root_command+=" && /usr/sbin/sshd -t -f '$daemon_config'"
root_command+=" && (/bin/launchctl bootout 'system/$label' >/dev/null 2>&1 || true)"
root_command+=" && /bin/launchctl enable 'system/$label'"
root_command+=" && /bin/launchctl bootstrap system '$daemon_plist'"
root_command+=" && /bin/launchctl kickstart -k 'system/$label'"
run_as_administrator "$root_command"

for _ in {1..20}; do
  if nc -z -w 1 "$tailscale_ip" 22 >/dev/null 2>&1; then
    inspect
    exit 0
  fi
  sleep 0.25
done

echo 'The private SSH daemon did not become reachable on the Tailscale address.' >&2
exit 1
