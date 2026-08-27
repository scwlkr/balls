#!/usr/bin/env bash
set -euo pipefail

readonly lab_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly project="balls-revit-server-2027-lab"
readonly container="balls-revit-server-2027-lab"
readonly config_root="/home/scwlkr/.config/balls-labs/revit-server-2027"
readonly private_env="${config_root}/private.env"
readonly state_root="/home/scwlkr/.local/share/balls-lab/revit-server-2027"
readonly mode_file="${state_root}/network-mode"
readonly image_digest="sha256:0cff9eb0e7aee9953e55bc682852ca4fdca233145a58ae1ec94f0b0c01a2ed30"

say() { printf '%s\n' "$*"; }
fail() { say "BLOCKED — $*" >&2; exit 1; }

require_private_env() {
  [[ -f "${private_env}" ]] || fail "create ${private_env} with BALLS_REVIT_SERVER_PASSWORD without printing it"
  local mode
  mode="$(stat -c '%a' "${private_env}")"
  [[ "${mode}" == "600" ]] || fail "${private_env} must have mode 0600"
}

compose() {
  local mode="${1}"
  shift
  local overlay="${lab_dir}/compose.${mode}.yaml"
  docker compose --project-name "${project}" --env-file "${private_env}" \
    --file "${lab_dir}/compose.yaml" --file "${overlay}" "$@"
}

container_running() {
  [[ "$(docker inspect --format '{{.State.Running}}' "$1" 2>/dev/null || true)" == "true" ]]
}

check_mutual_exclusion() {
  for name in omarchy-windows omarchy-windows-neptune revit-neptune-lab; do
    container_running "${name}" && fail "${name} is running; save work and stop it with its own manager"
  done
  if container_running balls-issue61-provider-desktop; then
    say "WARNING — balls-issue61-provider-desktop is running; stop it before decisive evidence"
  fi
  local available_kib
  available_kib="$(awk '/MemAvailable:/ { print $2 }' /proc/meminfo)"
  (( available_kib >= 10 * 1024 * 1024 )) || fail "less than 10 GiB memory is available"
}

port_free() {
  ! ss -H -ltn "sport = :$1" | grep -q .
}

network_free_or_owned() {
  local name="$1" expected="$2" observed all_subnets
  observed="$(docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}' "${name}" 2>/dev/null || true)"
  [[ -z "${observed}" || "${observed}" == "${expected}" ]] || fail "network ${name} has unexpected subnet ${observed}"
  all_subnets="$(docker network ls --format '{{.Name}}' | while read -r network; do
    [[ "${network}" == "${name}" ]] || docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}}{{"\n"}}{{end}}' "${network}" 2>/dev/null
  done; ip -4 route show | awk '$1 ~ /^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+\/[0-9]+$/ { print $1 }')"
  python3 - "${expected}" ${all_subnets} <<'PY' || fail "reserved subnet ${expected} overlaps an existing Docker network"
import ipaddress
import sys

target = ipaddress.ip_network(sys.argv[1])
for value in sys.argv[2:]:
    if value and target.overlaps(ipaddress.ip_network(value)):
        raise SystemExit(1)
PY
}

preflight() {
  [[ -e /dev/kvm && -e /dev/net/tun ]] || fail "KVM or TUN is unavailable"
  require_private_env
  compose bootstrap config --quiet
  compose acceptance config --quiet
  local digest
  digest="$(docker image inspect --format '{{index .RepoDigests 0}}' "${image_digest}" 2>/dev/null || true)"
  [[ "${digest}" == *"${image_digest}" ]] || fail "the pinned Dockurr image is unavailable"
  check_mutual_exclusion
  port_free 8027 || fail "loopback console port 8027 is in use"
  port_free 3397 || fail "loopback RDP port 3397 is in use"
  network_free_or_owned balls-revit-server-2027-bootstrap 172.29.26.0/24
  network_free_or_owned balls-revit-server-2027-lab 172.29.27.0/24
  say "PASS — pinned runtime, KVM, memory, ports, and reserved network identities are ready"
}

ensure_state_root() {
  install -d -m 700 "${state_root}" "${state_root}/system" "${state_root}/data" "${state_root}/evidence" "${state_root}/media"
}

bootstrap_start() {
  preflight
  [[ ! -f "${mode_file}" || "$(<"${mode_file}")" == "bootstrap" ]] || fail "the lab is already isolated for acceptance"
  ensure_state_root
  printf 'bootstrap\n' > "${mode_file}"
  compose bootstrap up -d
  say "PASS — bootstrap network only; use solely for OS updates and official in-guest downloads"
}

isolate() {
  require_private_env
  container_running "${container}" && fail "shut Windows down cleanly and stop the lab before isolation"
  compose bootstrap down
  [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] || fail "both lab disk files must already exist"
  printf 'acceptance\n' > "${mode_file}"
  say "PASS — bootstrap attachment removed; acceptance network selected"
}

start_acceptance() {
  preflight
  [[ -f "${mode_file}" && "$(<"${mode_file}")" == "acceptance" ]] || fail "run isolate after preparation"
  [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] || fail "both lab disk files must exist"
  compose acceptance up -d
  say "PASS — isolated acceptance lab started"
}

status() {
  local mode="not-created"
  [[ -f "${mode_file}" ]] && mode="$(<"${mode_file}")"
  local running="false"
  container_running "${container}" && running="true"
  say "Lab: ${container}"
  say "Mode: ${mode}"
  say "Running: ${running}"
  say "System disk: $([[ -f "${state_root}/system/data.img" ]] && printf present || printf absent)"
  say "Data disk: $([[ -f "${state_root}/data/data2.img" ]] && printf present || printf absent)"
}

stop_lab() {
  require_private_env
  local mode="acceptance"
  [[ -f "${mode_file}" ]] && mode="$(<"${mode_file}")"
  compose "${mode}" stop || true
  compose "${mode}" down
  say "PASS — lab stopped; disk directories preserved"
}

logs() {
  docker logs --tail 200 "${container}" 2>&1 \
    | sed -E 's/(PASSWORD|USERNAME|KEY|TOKEN|SECRET)=?[^[:space:]]*/\1=[REDACTED]/Ig'
}

recover() {
  require_private_env
  container_running "${container}" && fail "recovery requires a stopped lab"
  [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] || fail "missing disks block recovery"
  stop_lab
  start_acceptance
}

case "${1:-}" in
  preflight) preflight ;;
  bootstrap-start) bootstrap_start ;;
  isolate) isolate ;;
  start) start_acceptance ;;
  console) xdg-open http://127.0.0.1:8027/ >/dev/null 2>&1 ;;
  status) status ;;
  stop) stop_lab ;;
  logs) logs ;;
  recover) recover ;;
  *) fail "usage: manage.sh preflight|bootstrap-start|isolate|start|console|status|stop|logs|recover" ;;
esac
