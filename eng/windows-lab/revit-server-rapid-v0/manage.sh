#!/usr/bin/env bash
set -euo pipefail

readonly lab_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly project="balls-revit-server-2027-lab"
readonly container="balls-revit-server-2027-lab"
readonly config_root="/home/scwlkr/.config/balls-labs/revit-server-2027"
readonly private_env="${config_root}/private.env"
readonly state_root="/home/scwlkr/.local/share/balls-lab/revit-server-2027"
readonly mode_file="${state_root}/network-mode"
readonly ownership_marker=".balls-revit-server-2027-lab"
readonly marker_value="balls-revit-server-2027-lab:v1"
readonly trusted_media_name="Revit_Server_2027_win_db.sfx.exe"
readonly trusted_media_size=912600144
readonly trusted_media_sha256="295b30779868b9d58d78d9ff4353e4b9c6412418274a8034db6c6e7e0d348518"
readonly system_disk_size=171798691840
readonly data_disk_size=137438953472
readonly image_digest="sha256:0cff9eb0e7aee9953e55bc682852ca4fdca233145a58ae1ec94f0b0c01a2ed30"
readonly image_ref="docker.io/dockurr/windows@${image_digest}"

say() { printf '%s\n' "$*"; }
fail() { say "BLOCKED — $*" >&2; exit 1; }

require_private_env() {
  [[ -f "${private_env}" && ! -L "${private_env}" ]] || fail "create ${private_env} as a regular non-symlink file without printing it"
  local mode owner links
  mode="$(stat -c '%a' "${private_env}")"
  owner="$(stat -c '%u' "${private_env}")"
  links="$(stat -c '%h' "${private_env}")"
  [[ "${mode}" == "600" ]] || fail "${private_env} must have mode 0600"
  [[ "${owner}" == "$(id -u)" ]] || fail "${private_env} must be owned by the current user"
  [[ "${links}" == "1" ]] || fail "${private_env} must have exactly one hard link"
}

assert_owned_directory() {
  local path="$1"
  [[ -d "${path}" && ! -L "${path}" ]] || fail "${path} must be a real directory, not a link"
  [[ "$(stat -c '%u' "${path}")" == "$(id -u)" ]] || fail "${path} has a foreign owner"
  [[ "$(realpath -e -- "${path}")" == "${path}" ]] || fail "${path} does not resolve to its reserved canonical path"
  [[ "$(stat -c '%a' "${path}")" == "700" ]] || fail "${path} must have mode 0700"
}

assert_owned_regular() {
  local path="$1"
  [[ -f "${path}" && ! -L "${path}" ]] || fail "${path} must be a regular non-symlink file"
  [[ "$(stat -c '%u' "${path}")" == "$(id -u)" ]] || fail "${path} has a foreign owner"
  [[ "$(stat -c '%h' "${path}")" == "1" ]] || fail "${path} must have exactly one hard link"
}

validate_marker() {
  local directory="$1"
  local marker="${directory}/${ownership_marker}"
  assert_owned_regular "${marker}"
  [[ "$(<"${marker}")" == "${marker_value}" ]] || fail "${directory} has no valid lab ownership marker"
}

validate_directory_entries() {
  local directory="$1" allowed_pattern="$2" entry name
  while IFS= read -r -d '' entry; do
    name="${entry##*/}"
    [[ "${name}" =~ ${allowed_pattern} ]] || fail "foreign entry ${entry} blocks lab adoption"
  done < <(find "${directory}" -mindepth 1 -maxdepth 1 -print0)
}

validate_evidence_or_media_entries() {
  local directory="$1" entry
  while IFS= read -r -d '' entry; do
    [[ "${entry##*/}" == "${ownership_marker}" ]] || assert_owned_regular "${entry}"
  done < <(find "${directory}" -mindepth 1 -maxdepth 1 -print0)
}

validate_disk() {
  local path="$1" expected_size="$2" identity_path="$3" observed_identity
  if [[ ! -e "${path}" && ! -L "${path}" ]]; then
    [[ ! -e "${identity_path}" && ! -L "${identity_path}" ]] || fail "${identity_path} exists without its disk"
    return
  fi
  assert_owned_regular "${path}"
  [[ "$(stat -c '%s' "${path}")" == "${expected_size}" ]] || fail "${path} has an unexpected logical size"
  assert_owned_regular "${identity_path}"
  observed_identity="$(stat -c '%d:%i' "${path}")"
  [[ "$(<"${identity_path}")" == "${observed_identity}" ]] || fail "${path} was substituted after its device/inode identity was recorded"
}

validate_state_root() {
  [[ ! -e "${state_root}" && ! -L "${state_root}" ]] && return
  assert_owned_directory "${state_root}"
  validate_marker "${state_root}"
  validate_directory_entries "${state_root}" '^(.balls-revit-server-2027-lab|system|data|evidence|media|network-mode)$'
  local directory
  for directory in system data evidence media; do
    assert_owned_directory "${state_root}/${directory}"
    validate_marker "${state_root}/${directory}"
  done
  validate_directory_entries "${state_root}/system" '^(.balls-revit-server-2027-lab|data.img|data.img.identity|setup.img|win2022-eval.iso|windows.ver|windows.base|windows.mac|windows.rom|windows.vars|windows.boot)$'
  validate_directory_entries "${state_root}/data" '^(.balls-revit-server-2027-lab|data2.img|data2.img.identity)$'
  validate_evidence_or_media_entries "${state_root}/system"
  validate_evidence_or_media_entries "${state_root}/data"
  validate_evidence_or_media_entries "${state_root}/evidence"
  validate_evidence_or_media_entries "${state_root}/media"
  validate_disk "${state_root}/system/data.img" "${system_disk_size}" "${state_root}/system/data.img.identity"
  validate_disk "${state_root}/data/data2.img" "${data_disk_size}" "${state_root}/data/data2.img.identity"
  if [[ -e "${mode_file}" || -L "${mode_file}" ]]; then
    assert_owned_regular "${mode_file}"
    [[ "$(<"${mode_file}")" =~ ^(bootstrap|acceptance)$ ]] || fail "the lab network-mode marker is invalid"
    [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] \
      || fail "an initialized lab must keep both reserved disk files"
  fi
}

write_marker() {
  local directory="$1"
  printf '%s\n' "${marker_value}" > "${directory}/${ownership_marker}"
  chmod 600 "${directory}/${ownership_marker}"
}

initialize_state_root() {
  if [[ -f "${state_root}/${ownership_marker}" && ! -L "${state_root}/${ownership_marker}" ]]; then
    validate_state_root
    say "PASS — owner-marked lab state is already initialized"
    return
  fi
  if [[ -e "${state_root}" || -L "${state_root}" ]]; then
    [[ -d "${state_root}" && ! -L "${state_root}" ]] || fail "the existing state root is not a real directory"
    [[ "$(stat -c '%u' "${state_root}")" == "$(id -u)" ]] || fail "the existing state root has a foreign owner"
    [[ "$(realpath -e -- "${state_root}")" == "${state_root}" ]] || fail "the existing state root is not canonical"
    validate_directory_entries "${state_root}" '^media$'
    local media_directory="${state_root}/media" media_path="${state_root}/media/${trusted_media_name}"
    [[ -d "${media_directory}" && ! -L "${media_directory}" ]] || fail "only the exact cached-media directory can be adopted"
    [[ "$(stat -c '%u' "${media_directory}")" == "$(id -u)" ]] || fail "the cached-media directory has a foreign owner"
    [[ "$(realpath -e -- "${media_directory}")" == "${media_directory}" ]] || fail "the cached-media directory is not canonical"
    validate_directory_entries "${media_directory}" "^${trusted_media_name}$"
    assert_owned_regular "${media_path}"
    [[ "$(stat -c '%s' "${media_path}")" == "${trusted_media_size}" ]] || fail "cached media size is not the trusted identity"
    [[ "$(sha256sum -- "${media_path}" | awk '{print $1}')" == "${trusted_media_sha256}" ]] || fail "cached media hash is not the trusted identity"
    chmod 700 "${state_root}" "${media_directory}"
    chmod 600 "${media_path}"
  else
    install -d -m 700 "${state_root}" "${state_root}/media"
  fi
  install -d -m 700 "${state_root}/system" "${state_root}/data" "${state_root}/evidence"
  local directory
  for directory in "${state_root}" "${state_root}/system" "${state_root}/data" "${state_root}/evidence" "${state_root}/media"; do
    write_marker "${directory}"
  done
  validate_state_root
  say "PASS — exact empty/cached-media-only state initialized with owner-only lab markers"
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

validate_container_identity() {
  docker inspect "${container}" >/dev/null 2>&1 || return 0
  local labels image
  labels="$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}|{{index .Config.Labels "com.docker.compose.service"}}' "${container}")"
  image="$(docker inspect --format '{{.Config.Image}}' "${container}")"
  [[ "${labels}" == "${project}|windows" && "${image}" == "${image_ref}" ]] \
    || fail "container name ${container} is not the exact lab-owned pinned container"
}

check_mutual_exclusion() {
  for name in omarchy-windows omarchy-windows-neptune revit-neptune-lab; do
    container_running "${name}" && fail "${name} is running; save work and stop it with its own manager"
  done
  container_running balls-issue61-provider-desktop && fail "balls-issue61-provider-desktop is running; save work and stop it before operating this lab"
  local available_kib
  available_kib="$(awk '/MemAvailable:/ { print $2 }' /proc/meminfo)"
  (( available_kib >= 10 * 1024 * 1024 )) || fail "less than 10 GiB memory is available"
}

port_free() {
  ! ss -H -ltn "sport = :$1" | grep -q .
}

udp_port_free() {
  ! ss -H -lun "sport = :$1" | grep -q .
}

network_free_or_owned() {
  local name="$1" expected="$2" gateway="$3" internal="$4" observed all_subnets owner shape
  observed="$(docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}' "${name}" 2>/dev/null || true)"
  [[ -z "${observed}" || "${observed}" == "${expected}" ]] || fail "network ${name} has unexpected subnet ${observed}"
  if [[ -n "${observed}" ]]; then
    owner="$(docker network inspect --format '{{index .Labels "com.docker.compose.project"}}' "${name}")"
    [[ "${owner}" == "${project}" ]] || fail "network name ${name} is not owned by the reserved Compose project"
    shape="$(docker network inspect --format '{{.Driver}}|{{.Internal}}|{{range .IPAM.Config}}{{.Subnet}}|{{.Gateway}}{{end}}' "${name}")"
    [[ "${shape}" == "bridge|${internal}|${expected}|${gateway}" ]] \
      || fail "existing network ${name} has the wrong driver/internal/subnet/gateway shape"
  fi
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
  validate_state_root
  compose bootstrap config --quiet
  compose acceptance config --quiet
  local digest
  digest="$(docker image inspect --format '{{join .RepoDigests "\n"}}' "${image_ref}" 2>/dev/null || true)"
  [[ "${digest}" == *"${image_digest}" ]] || fail "the pinned Dockurr image is unavailable"
  validate_container_identity
  check_mutual_exclusion
  port_free 8027 || fail "loopback console port 8027 is in use"
  port_free 3397 || fail "loopback RDP port 3397 is in use"
  udp_port_free 3397 || fail "loopback UDP RDP port 3397 is in use"
  network_free_or_owned balls-revit-server-2027-bootstrap 172.29.26.0/24 172.29.26.1 false
  network_free_or_owned balls-revit-server-2027-lab 172.29.27.0/24 172.29.27.1 true
  say "PASS — pinned runtime, KVM, memory, ports, and reserved network identities are ready"
}

ensure_state_root() {
  [[ -e "${state_root}" && ! -L "${state_root}" ]] || fail "run initialize before operating the lab"
  validate_state_root
}

record_disk_identity() {
  local path="$1" expected_size="$2" identity_path="$3"
  assert_owned_regular "${path}"
  [[ "$(stat -c '%s' "${path}")" == "${expected_size}" ]] || fail "${path} has an unexpected logical size"
  if [[ -e "${identity_path}" || -L "${identity_path}" ]]; then
    validate_disk "${path}" "${expected_size}" "${identity_path}"
    return
  fi
  printf '%s\n' "$(stat -c '%d:%i' "${path}")" > "${identity_path}"
  chmod 600 "${identity_path}"
  validate_disk "${path}" "${expected_size}" "${identity_path}"
}

record_initial_disk_identities() {
  local deadline=$((SECONDS + 2700))
  while (( SECONDS < deadline )); do
    if [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] \
      && [[ "$(stat -c '%s' "${state_root}/system/data.img")" == "${system_disk_size}" ]] \
      && [[ "$(stat -c '%s' "${state_root}/data/data2.img")" == "${data_disk_size}" ]]; then
      record_disk_identity "${state_root}/system/data.img" "${system_disk_size}" "${state_root}/system/data.img.identity"
      record_disk_identity "${state_root}/data/data2.img" "${data_disk_size}" "${state_root}/data/data2.img.identity"
      return
    fi
    sleep 2
  done
  fail "Dockurr did not create both exact disk shapes within 45 minutes; no new identity was approved"
}

validate_partial_bootstrap_state() {
  assert_owned_directory "${state_root}"
  validate_marker "${state_root}"
  validate_directory_entries "${state_root}" '^(.balls-revit-server-2027-lab|system|data|evidence|media)$'
  local directory
  for directory in system data evidence media; do
    assert_owned_directory "${state_root}/${directory}"
    validate_marker "${state_root}/${directory}"
  done
  validate_directory_entries "${state_root}/system" '^(.balls-revit-server-2027-lab|tmp|data.img|data.img.identity|setup.img|win2022-eval.iso|windows.ver|windows.base|windows.mac|windows.rom|windows.vars|windows.boot)$'
  validate_directory_entries "${state_root}/data" '^(.balls-revit-server-2027-lab|data2.img|data2.img.identity)$'
  validate_evidence_or_media_entries "${state_root}/evidence"
  validate_evidence_or_media_entries "${state_root}/media"
  [[ ! -e "${mode_file}" && ! -L "${mode_file}" ]] || fail "resume-bootstrap is only for a first bootstrap before network-mode is committed"
  if [[ -e "${state_root}/system/tmp" || -L "${state_root}/system/tmp" ]]; then
    local temporary="${state_root}/system/tmp" owner entry
    [[ -d "${temporary}" && ! -L "${temporary}" ]] || fail "the bootstrap temporary path is not a real directory"
    [[ "$(realpath -e -- "${temporary}")" == "${temporary}" ]] || fail "the bootstrap temporary path is not canonical"
    owner="$(stat -c '%u' "${temporary}")"
    [[ "${owner}" == "0" || "${owner}" == "$(id -u)" ]] || fail "the bootstrap temporary path has a foreign owner"
    while IFS= read -r -d '' entry; do
      [[ ! -L "${entry}" ]] || fail "a linked bootstrap temporary entry blocks resume"
      owner="$(stat -c '%u' "${entry}")"
      [[ "${owner}" == "0" || "${owner}" == "$(id -u)" ]] || fail "a foreign bootstrap temporary entry blocks resume"
      [[ ! -f "${entry}" || "$(stat -c '%h' "${entry}")" == "1" ]] || fail "a hard-linked bootstrap temporary file blocks resume"
    done < <(find "${temporary}" -mindepth 1 -xdev -print0)
  fi
  validate_container_identity
  container_running "${container}" || fail "resume-bootstrap requires the exact lab container to be running"
  attest_selected_network balls-revit-server-2027-bootstrap 172.29.26.0/24 172.29.26.1 172.29.26.2 false
}

resume_bootstrap() {
  require_private_env
  validate_partial_bootstrap_state
  record_initial_disk_identities
  local cleanup_deadline=$((SECONDS + 300))
  while [[ -e "${state_root}/system/tmp" || -L "${state_root}/system/tmp" ]] && (( SECONDS < cleanup_deadline )); do
    sleep 2
  done
  [[ ! -e "${state_root}/system/tmp" && ! -L "${state_root}/system/tmp" ]] \
    || fail "Dockurr created the disks but did not clear its exact bootstrap temporary directory within five minutes"
  printf 'bootstrap\n' > "${mode_file}"
  chmod 600 "${mode_file}"
  validate_state_root
  say "PASS — resumed bootstrap recorded both first-created disk identities and exact bootstrap mode"
}

attest_selected_network() {
  local name="$1" subnet="$2" gateway="$3" address="$4" internal="$5"
  local attached network_shape
  attached="$(docker inspect --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}}{{"\n"}}{{end}}' "${container}")"
  [[ "${attached}" == "${name}" ]] || fail "container is not attached to exactly the selected ${name} network"
  network_shape="$(docker network inspect --format '{{.Driver}}|{{.Internal}}|{{range .IPAM.Config}}{{.Subnet}}|{{.Gateway}}{{end}}' "${name}")"
  [[ "${network_shape}" == "bridge|${internal}|${subnet}|${gateway}" ]] || fail "network ${name} does not match the reserved driver/internal/subnet/gateway identity"
  [[ "$(docker inspect --format "{{(index .NetworkSettings.Networks \"${name}\").IPAddress}}" "${container}")" == "${address}" ]] \
    || fail "container does not have reserved address ${address} on ${name}"
}

bootstrap_start() {
  preflight
  [[ ! -f "${mode_file}" || "$(<"${mode_file}")" == "bootstrap" ]] || fail "the lab is already isolated for acceptance"
  ensure_state_root
  local first_start=false
  if [[ ! -f "${state_root}/system/data.img" && ! -f "${state_root}/data/data2.img" ]]; then
    first_start=true
  else
    [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] \
      || fail "a partial disk set blocks bootstrap"
  fi
  compose bootstrap up -d
  attest_selected_network balls-revit-server-2027-bootstrap 172.29.26.0/24 172.29.26.1 172.29.26.2 false
  [[ "${first_start}" == "false" ]] || record_initial_disk_identities
  printf 'bootstrap\n' > "${mode_file}"
  chmod 600 "${mode_file}"
  validate_state_root
  say "PASS — bootstrap network only; use solely for OS updates and official in-guest downloads"
}

isolate() {
  require_private_env
  validate_container_identity
  container_running "${container}" && fail "shut Windows down cleanly and stop the lab before isolation"
  compose bootstrap down
  [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] || fail "both lab disk files must already exist"
  printf 'acceptance\n' > "${mode_file}"
  chmod 600 "${mode_file}"
  say "PASS — bootstrap attachment removed; acceptance network selected"
}

start_acceptance() {
  preflight
  [[ -f "${mode_file}" && "$(<"${mode_file}")" == "acceptance" ]] || fail "run isolate after preparation"
  [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] || fail "both lab disk files must exist"
  compose acceptance up -d
  attest_selected_network balls-revit-server-2027-lab 172.29.27.0/24 172.29.27.1 172.29.27.2 true
  say "PASS — isolated acceptance lab started"
}

status() {
  validate_state_root
  validate_container_identity
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
  validate_state_root
  validate_container_identity
  local mode="acceptance"
  [[ -f "${mode_file}" ]] && mode="$(<"${mode_file}")"
  if container_running "${container}"; then
    docker container kill --signal TERM "${container}" >/dev/null \
      || fail "the graceful TERM request failed; container and disks were preserved"
    local deadline=$((SECONDS + 120))
    while container_running "${container}" && (( SECONDS < deadline )); do
      sleep 2
    done
    container_running "${container}" \
      && fail "Windows did not stop within two minutes; it was not force-killed and Compose down was not run"
  fi
  compose "${mode}" down
  say "PASS — lab stopped; disk directories preserved"
}

recover() {
  require_private_env
  container_running "${container}" && fail "recovery requires a stopped lab"
  [[ -f "${state_root}/system/data.img" && -f "${state_root}/data/data2.img" ]] || fail "missing disks block recovery"
  stop_lab
  start_acceptance
}

case "${1:-}" in
  initialize) initialize_state_root ;;
  preflight) preflight ;;
  bootstrap-start) bootstrap_start ;;
  resume-bootstrap) resume_bootstrap ;;
  isolate) isolate ;;
  start) start_acceptance ;;
  console) xdg-open http://127.0.0.1:8027/ >/dev/null 2>&1 ;;
  status) status ;;
  stop) stop_lab ;;
  recover) recover ;;
  *) fail "usage: manage.sh initialize|preflight|bootstrap-start|resume-bootstrap|isolate|start|console|status|stop|recover" ;;
esac
