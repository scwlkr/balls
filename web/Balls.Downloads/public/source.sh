#!/usr/bin/env bash
set -euo pipefail

# macOS is source-only: this fetches one accepted release and does not install an app.
manifest_url="https://balls.wlkrlabs.com/channels/alpha.json"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
  echo "The Balls macOS developer lane currently targets Apple silicon." >&2
  exit 1
fi
for command in curl python3 git; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Required command is missing: $command" >&2
    exit 1
  fi
done

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

with open(sys.argv[1], encoding="utf-8") as stream:
    manifest = json.load(stream)

release = manifest.get("release", {})
delivery = manifest.get("platforms", {}).get("macos-arm64", {})
tag = release.get("tag", "")
commit = release.get("commit", "")
repository = delivery.get("repository", "")
documentation = delivery.get("documentation", "")

if manifest.get("schemaVersion") != 1 or manifest.get("channel") != "alpha":
    raise SystemExit("The Balls Alpha manifest has an unsupported schema or channel.")
if delivery.get("delivery") != "source-only":
    raise SystemExit("The Alpha manifest does not describe the macOS source-only lane.")
if not re.fullmatch(r"\d+\.\d+\.\d+-alpha\.\d+", tag):
    raise SystemExit("The Balls Alpha manifest has an invalid tag.")
if not re.fullmatch(r"[0-9a-f]{40}", commit):
    raise SystemExit("The Balls Alpha manifest has an invalid commit.")
if repository != "https://github.com/scwlkr/balls.git":
    raise SystemExit("The Balls Alpha manifest has an unexpected source repository.")
if not documentation.startswith("https://github.com/scwlkr/balls/"):
    raise SystemExit("The Balls Alpha manifest has an unexpected documentation URL.")

print(tag)
print(commit)
print(repository)
print(documentation)
PY

metadata=()
while IFS= read -r line; do
  metadata+=("$line")
done <"$metadata_path"
if [[ ${#metadata[@]} -ne 4 ]]; then
  echo "The Balls Alpha manifest returned incomplete macOS metadata." >&2
  exit 1
fi

tag="${metadata[0]}"
commit="${metadata[1]}"
repository="${metadata[2]}"
documentation="${metadata[3]}"
source_root="${BALLS_SOURCE_ROOT:-$HOME/Library/Application Support/Balls-Source}"
version_root="$source_root/$tag"

mkdir -p "$source_root"
if [[ -e "$version_root" ]]; then
  if [[ ! -d "$version_root/.git" ]]; then
    echo "Refusing to replace an existing non-repository path: $version_root" >&2
    exit 1
  fi
  if [[ "$(git -C "$version_root" remote get-url origin)" != "$repository" ]]; then
    echo "Refusing to use a source directory with a different origin: $version_root" >&2
    exit 1
  fi
else
  git clone --filter=blob:none --branch "$tag" --depth 1 "$repository" "$version_root"
fi

actual_commit="$(git -C "$version_root" rev-parse HEAD)"
if [[ "$actual_commit" != "$commit" ]]; then
  echo "The downloaded source commit does not match the accepted Alpha manifest." >&2
  exit 1
fi

echo "Verified Balls $tag source at $version_root"
echo "This is the macOS source-only developer lane, not a packaged or notarized app."
echo "Next steps: $documentation"
