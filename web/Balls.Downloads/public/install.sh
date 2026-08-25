#!/usr/bin/env bash
set -euo pipefail

manifest_url="https://balls.wlkrlabs.com/channels/alpha.json"

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
  echo "The published Balls Linux Alpha requires x64 Linux." >&2
  exit 1
fi

for command in curl python3 sha256sum unzip realpath dotnet grep; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Required command is missing: $command" >&2
    exit 1
  fi
done
if ! dotnet --list-runtimes | grep -Eq '^Microsoft\.AspNetCore\.App 10\.'; then
  echo "The Balls Linux Alpha requires the ASP.NET Core 10 runtime." >&2
  exit 1
fi

umask 077
temporary_root="$(mktemp -d)"
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT INT TERM

manifest_path="$temporary_root/alpha.json"
metadata_path="$temporary_root/metadata"
curl --proto '=https' --tlsv1.2 -fsSL "$manifest_url" -o "$manifest_path"

python3 - "$manifest_path" >"$metadata_path" <<'PY'
import json
import re
import sys
from pathlib import PurePosixPath
from urllib.parse import urlparse

with open(sys.argv[1], encoding="utf-8") as stream:
    manifest = json.load(stream)

if manifest.get("schemaVersion") != 1 or manifest.get("channel") != "alpha":
    raise SystemExit("The Balls Alpha manifest has an unsupported schema or channel.")

release = manifest.get("release", {})
tag = release.get("tag", "")
commit = release.get("commit", "")
if not re.fullmatch(r"\d+\.\d+\.\d+-alpha\.\d+", tag):
    raise SystemExit("The Balls Alpha manifest has an invalid tag.")
if not re.fullmatch(r"[0-9a-f]{40}", commit):
    raise SystemExit("The Balls Alpha manifest has an invalid commit.")

delivery = manifest.get("platforms", {}).get("linux-x64", {})
if delivery.get("delivery") != "package":
    raise SystemExit("The Alpha manifest does not contain a Linux package.")

assets = (delivery.get("archive", {}), delivery.get("checksum", {}), delivery.get("installer", {}))
name_patterns = (
    rf"balls-{re.escape(tag)}-canary-linux-x64-[0-9a-f]{{12}}\.zip",
    rf"balls-{re.escape(tag)}-canary-linux-x64-[0-9a-f]{{12}}\.zip\.sha256",
    r"Install-BallsCanary\.sh",
)
prefix = f"/scwlkr/balls/releases/download/{tag}/"
for asset, pattern in zip(assets, name_patterns, strict=True):
    name = asset.get("name", "")
    digest = asset.get("sha256", "")
    parsed = urlparse(asset.get("url", ""))
    if not re.fullmatch(pattern, name) or PurePosixPath(name).name != name:
        raise SystemExit(f"The Alpha manifest contains an invalid asset name: {name}")
    if not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise SystemExit(f"The Alpha manifest contains an invalid SHA-256 for {name}.")
    if parsed.scheme != "https" or parsed.netloc != "github.com":
        raise SystemExit(f"The Alpha manifest contains an unexpected download host for {name}.")
    if not parsed.path.startswith(prefix) or PurePosixPath(parsed.path).name != name:
        raise SystemExit(f"The Alpha manifest contains an unexpected download path for {name}.")

print(tag)
print(commit)
for asset in assets:
    print(asset["name"])
    print(asset["url"])
    print(asset["sha256"])
PY

mapfile -t metadata <"$metadata_path"
if [[ ${#metadata[@]} -ne 11 ]]; then
  echo "The Balls Alpha manifest returned incomplete Linux metadata." >&2
  exit 1
fi

tag="${metadata[0]}"
commit="${metadata[1]}"
archive_name="${metadata[2]}"
archive_url="${metadata[3]}"
archive_hash="${metadata[4]}"
checksum_name="${metadata[5]}"
checksum_url="${metadata[6]}"
checksum_hash="${metadata[7]}"
installer_name="${metadata[8]}"
installer_url="${metadata[9]}"
installer_hash="${metadata[10]}"

download_verified() {
  local name="$1"
  local url="$2"
  local expected_hash="$3"
  local path="$temporary_root/$name"
  curl --proto '=https' --tlsv1.2 -fsSL "$url" -o "$path"
  if ! printf '%s  %s\n' "$expected_hash" "$path" | sha256sum --check --status; then
    echo "SHA-256 verification failed for $name." >&2
    exit 1
  fi
}

download_verified "$archive_name" "$archive_url" "$archive_hash"
download_verified "$checksum_name" "$checksum_url" "$checksum_hash"
download_verified "$installer_name" "$installer_url" "$installer_hash"

echo "Verified Balls $tag (${commit:0:12})."
echo "The current Alpha is unsigned. No system trust policy is bypassed."
bash "$temporary_root/$installer_name" \
  "$temporary_root/$archive_name" \
  "$temporary_root/$checksum_name"
