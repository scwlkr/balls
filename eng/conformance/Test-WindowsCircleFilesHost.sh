#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo 'Usage: eng/conformance/Test-WindowsCircleFilesHost.sh --target-profile <json> --receipt <json>' >&2
  exit 2
}

if [[ $# -ne 4 || $1 != --target-profile || $3 != --receipt ]]; then
  usage
fi

target_profile=$2
receipt=$4
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repository_root=$(cd -- "$script_directory/../.." && pwd -P)
dotnet_command=${BALLS_DOTNET_COMMAND:-dotnet}

if [[ $(uname -s) != Linux ]]; then
  echo 'windows-conformance: linux_required' >&2
  exit 3
fi

for required_command in git scp ssh timeout "$dotnet_command"; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "windows-conformance: required_command_missing" >&2
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

work_directory=$(mktemp -d -t balls-windows-host-conformance.XXXXXXXXXX)
cleanup() {
  find "$work_directory" -type f -exec chmod u+w {} + 2>/dev/null || true
  rm -rf -- "$work_directory"
}
trap cleanup EXIT INT TERM

cli_directory=$work_directory/input/balls
daemon_directory=$work_directory/input/ballsd
output_directory=$work_directory/output
mkdir -p -- "$cli_directory" "$daemon_directory" "$output_directory"

cd -- "$repository_root"
"$dotnet_command" restore Balls.slnx --locked-mode
"$dotnet_command" restore src/Balls.Cli/Balls.Cli.csproj \
  --runtime win-x64 \
  -p:NuGetLockFilePath=obj/packages.win-x64.lock.json
"$dotnet_command" restore src/Balls.Daemon/Balls.Daemon.csproj \
  --runtime win-x64 \
  -p:NuGetLockFilePath=obj/packages.win-x64.lock.json

# The non-distributable Debug package keeps the repository's bounded hosting fault injection.
# The receipt names this configuration; it is never a release artifact or user package.
"$dotnet_command" publish src/Balls.Cli/Balls.Cli.csproj \
  --configuration Debug \
  --runtime win-x64 \
  --self-contained true \
  --no-restore \
  --output "$cli_directory"
"$dotnet_command" publish src/Balls.Daemon/Balls.Daemon.csproj \
  --configuration Debug \
  --runtime win-x64 \
  --self-contained true \
  --no-restore \
  --output "$daemon_directory"
"$dotnet_command" run --project eng/Balls.Canary --configuration Release --no-restore -- package \
  --repository-root "$repository_root" \
  --cli-directory "$cli_directory" \
  --daemon-directory "$daemon_directory" \
  --output-directory "$output_directory" \
  --platform windows \
  --architecture x64 \
  --commit "$commit"

mapfile -t packages < <(find "$output_directory" -maxdepth 1 -type f -name '*.zip' -print)
if [[ ${#packages[@]} -ne 1 ]]; then
  echo 'windows-conformance: package_output_ambiguous' >&2
  exit 3
fi
package=${packages[0]}
checksum=$package.sha256
if [[ ! -f $checksum ]]; then
  echo 'windows-conformance: package_checksum_invalid' >&2
  exit 3
fi

timeout --foreground --signal=INT --kill-after=195s 15m \
  "$dotnet_command" run --project eng/Balls.WindowsConformance --configuration Release --no-restore -- \
    host-run \
    --target-profile "$target_profile" \
    --package "$package" \
    --checksum "$checksum" \
    --expected-commit "$commit" \
    --receipt "$receipt"
