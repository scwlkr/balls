#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: Test-LinuxCanary.sh <package.zip> <package.zip.sha256> <installer>" >&2
  exit 2
fi

package_path="$(realpath "$1")"
checksum_path="$(realpath "$2")"
installer_path="$(realpath "$3")"
smoke_root="$(mktemp -d)"
install_root="$smoke_root/install"
runtime_root="$smoke_root/runtime"
socket_path="$runtime_root/control.sock"
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

bash "$installer_path" \
  "$package_path" \
  "$checksum_path" \
  "$install_root" \
  "$runtime_root" \
  "Balls Linux Canary Smoke"

daemon_pid="$(cat "$install_root/ballsd.pid")"
version_root="$(find "$install_root/versions" -mindepth 1 -maxdepth 1 -type d -print -quit)"
cli="$version_root/balls/balls"
daemon="$version_root/ballsd/ballsd"
if [[ ! -f "$version_root/ballsd/wwwroot/index.html" ]]; then
  echo "Installed Linux Canary is missing the browser bundle." >&2
  exit 1
fi

"$cli" --output json --pipe-name "$socket_path" status >"$smoke_root/status-before.json"
"$cli" --output json --pipe-name "$socket_path" circle create \
  "Canary Circle" \
  --owner "Canary Owner" \
  --request-id "0198c2d8-b000-7000-8000-000000000601" \
  >"$smoke_root/create.json"
"$cli" --output json --pipe-name "$socket_path" circle list >"$smoke_root/list-before.json"

read -r node_id circle_id < <(python3 - \
  "$smoke_root/status-before.json" \
  "$smoke_root/create.json" \
  "$smoke_root/list-before.json" <<'PY'
import json
import sys

status = json.load(open(sys.argv[1], encoding="utf-8"))["result"]
created = json.load(open(sys.argv[2], encoding="utf-8"))["result"]
listed = json.load(open(sys.argv[3], encoding="utf-8"))["result"]
assert status["node"]["displayName"] == "Balls Linux Canary Smoke"
assert created["circle"]["name"] == "Canary Circle"
assert created["members"][0]["displayName"] == "Canary Owner"
assert created["nodes"][0]["id"] == status["node"]["id"]
assert listed["circles"][0]["id"] == created["circle"]["id"]
print(status["node"]["id"], created["circle"]["id"])
PY
)

mkdir -p "$smoke_root/bin"
cat >"$smoke_root/bin/xdg-open" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
printf '%s' "$1" >"$BALLS_BROWSER_CAPTURE"
SH
chmod 700 "$smoke_root/bin/xdg-open"
export BALLS_BROWSER_CAPTURE="$smoke_root/browser-url"
PATH="$smoke_root/bin:$PATH" "$cli" --pipe-name "$socket_path" ui >"$smoke_root/ui.out"
grep -Fx "Opened the local Balls workspace." "$smoke_root/ui.out"

launch_url="$(cat "$BALLS_BROWSER_CAPTURE")"
browser_port="$(python3 - "$launch_url" <<'PY'
import sys
import urllib.parse

url = urllib.parse.urlparse(sys.argv[1])
assert url.scheme == "http"
assert url.hostname == "127.0.0.1"
assert url.fragment.startswith("launch=")
assert not url.query
print(url.port)
PY
)"
listeners="$(ss -ltnH "sport = :$browser_port")"
if [[ -z "$listeners" ]] || grep -Eq '(^|[[:space:]])(0\.0\.0\.0|\[::\]):' <<<"$listeners"; then
  echo "Linux Canary browser listener is not loopback-only." >&2
  exit 1
fi

google-chrome \
  --headless=new \
  --no-sandbox \
  --disable-gpu \
  --virtual-time-budget=5000 \
  --dump-dom "$launch_url" \
  >"$smoke_root/browser-before.html" \
  2>"$smoke_root/chrome-before.err"
grep -F "Canary Circle" "$smoke_root/browser-before.html" >/dev/null
grep -F "Canary Owner" "$smoke_root/browser-before.html" >/dev/null
grep -F "Balls Linux Canary Smoke" "$smoke_root/browser-before.html" >/dev/null
rm -f -- "$BALLS_BROWSER_CAPTURE"

terminate_daemon
if [[ -e "$socket_path" ]]; then
  echo "Linux Canary daemon did not clean up its Unix-domain socket." >&2
  exit 1
fi

"$daemon" \
  --data-directory "$install_root/state" \
  --pipe-name "$socket_path" \
  --node-name "Renamed Linux Host" \
  >"$install_root/ballsd.out" \
  2>"$install_root/ballsd.err" &
daemon_pid=$!
printf '%s' "$daemon_pid" >"$install_root/ballsd.pid"

ready=false
for _ in $(seq 1 100); do
  if "$cli" --output json --pipe-name "$socket_path" status >"$smoke_root/status-after.json" 2>/dev/null; then
    ready=true
    break
  fi
  sleep 0.1
done
if [[ "$ready" != true ]]; then
  cat "$install_root/ballsd.err" >&2
  echo "Restarted Linux Canary daemon did not become ready." >&2
  exit 1
fi
"$cli" --output json --pipe-name "$socket_path" circle list >"$smoke_root/list-after.json"

python3 - \
  "$smoke_root/status-after.json" \
  "$smoke_root/list-after.json" \
  "$node_id" \
  "$circle_id" <<'PY'
import json
import sys

status = json.load(open(sys.argv[1], encoding="utf-8"))["result"]
listed = json.load(open(sys.argv[2], encoding="utf-8"))["result"]
assert status["node"]["id"] == sys.argv[3]
assert status["node"]["displayName"] == "Balls Linux Canary Smoke"
assert listed["circles"][0]["id"] == sys.argv[4]
PY

PATH="$smoke_root/bin:$PATH" "$cli" --pipe-name "$socket_path" ui >"$smoke_root/ui-after.out"
google-chrome \
  --headless=new \
  --no-sandbox \
  --disable-gpu \
  --virtual-time-budget=5000 \
  --dump-dom "$(cat "$BALLS_BROWSER_CAPTURE")" \
  >"$smoke_root/browser-after.html" \
  2>"$smoke_root/chrome-after.err"
grep -F "Canary Circle" "$smoke_root/browser-after.html" >/dev/null

terminate_daemon
if [[ -e "$socket_path" ]]; then
  echo "Linux Canary daemon did not clean up its Unix-domain socket after restart." >&2
  exit 1
fi

echo "Linux Canary install, structured CLI, browser, and restart smoke passed from fresh state."
