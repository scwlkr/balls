#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: Build-TwoLaptopPilotBundle.sh <runtime-archive.zip> <output-bundle.zip>" >&2
  exit 64
fi

runtime_archive=$(realpath "$1")
output_bundle=$(realpath -m "$2")
expected_hash=96e742abcf1a35efb5722d54dc88dc26471cafdeb501672997de49e5749613b5
expected_commit=67974f2de6502d99a55378e9da5aabf5e4293cc7
script_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

if [[ -e "$output_bundle" ]]; then
  echo "refusing to overwrite existing output: $output_bundle" >&2
  exit 65
fi

actual_hash=$(sha256sum "$runtime_archive" | awk '{print $1}')
if [[ "$actual_hash" != "$expected_hash" ]]; then
  echo "runtime archive hash mismatch: $actual_hash" >&2
  exit 66
fi

work_root=$(mktemp -d)
trap 'rm -rf -- "$work_root"' EXIT
stage_root="$work_root/stage"
mkdir -p "$stage_root/Balls"
unzip -q "$runtime_archive" -d "$stage_root/Balls"

for required in \
  "$stage_root/Balls/canary.json" \
  "$stage_root/Balls/SHA256SUMS" \
  "$stage_root/Balls/balls/balls.exe" \
  "$stage_root/Balls/ballsd/ballsd.exe"; do
  if [[ ! -f "$required" ]]; then
    echo "runtime archive is incomplete: $required" >&2
    exit 67
  fi
done

if ! grep -Fq "$expected_commit" "$stage_root/Balls/canary.json"; then
  echo "runtime archive contains the wrong commit" >&2
  exit 68
fi

cp "$script_root/Check-TwoLaptopPilotPackage.cmd" "$stage_root/CHECK PACKAGE.cmd"
cp "$script_root/START-HERE-TWO-LAPTOP-PILOT.txt" "$stage_root/START HERE.txt"

mkdir -p "$(dirname -- "$output_bundle")"
(
  cd "$stage_root"
  zip -q -r "$output_bundle" .
)
unzip -tq "$output_bundle"
sha256sum "$output_bundle" > "$output_bundle.sha256"

echo "Created $output_bundle"
echo "SHA-256 $(sha256sum "$output_bundle" | awk '{print $1}')"
