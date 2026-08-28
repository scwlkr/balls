#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo 'Usage: eng/conformance/Inspect-WindowsCircleFilesHostStorage.sh --target-profile <json> --receipt <json>' >&2
  exit 2
}

if [[ $# -ne 4 || $1 != --target-profile || $3 != --receipt ]]; then
  usage
fi

target_profile=$2
receipt=$4
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repository_root=$(cd -- "$script_directory/../.." && pwd -P)

if [[ $(uname -s) != Linux ]]; then
  echo 'windows-conformance: linux_required' >&2
  exit 3
fi

for required_command in dotnet git ssh timeout; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo 'windows-conformance: required_command_missing' >&2
    exit 3
  fi
done

if [[ ! -f $target_profile || -L $target_profile ]]; then
  echo 'windows-conformance: target_profile_invalid' >&2
  exit 3
fi

if [[ -e $receipt ]]; then
  echo 'windows-conformance: receipt_path_unsafe' >&2
  exit 3
fi

if [[ -n $(git -C "$repository_root" status --porcelain) ]]; then
  echo 'windows-conformance: repository_dirty' >&2
  exit 3
fi

commit=$(git -C "$repository_root" rev-parse --verify HEAD)
if [[ ! $commit =~ ^[0-9a-f]{40}$ ]]; then
  echo 'windows-conformance: expected_commit_invalid' >&2
  exit 3
fi

cd -- "$repository_root"
timeout --foreground --signal=INT --kill-after=15s 2m \
  dotnet run --project eng/Balls.WindowsConformance --configuration Release -- \
    host-storage-inspect \
    --target-profile "$target_profile" \
    --expected-commit "$commit" \
    --receipt "$receipt"
