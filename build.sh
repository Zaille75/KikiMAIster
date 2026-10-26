#!/usr/bin/env bash
# Builds the mod and puts the files to install in dist/<branch>/KikiMAIster/
# Usage: ./build.sh [beta|default]   (default: beta)
set -euo pipefail
branch="${1:-beta}"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_ROOT=~/.dotnet PATH=$PATH:~/.dotnet:~/.dotnet/tools
cd "$(dirname "$0")"
dotnet build src/KikiMAIster -c Release -p:GameBranch="$branch" -o "build/$branch" --nologo -v q
# Every Harmony patch must match exactly one game method (catches breakage after game updates).
check=$(dotnet run --project tools/PatchCheck -v q -- "lib/$branch" "build/$branch/KikiMAIster.dll")
echo "$check" | tail -1
if echo "$check" | grep -q '^FAIL'; then
  echo "$check" | grep '^FAIL'; echo "Patch check failed for branch $branch" >&2; exit 1
fi
dotnet test tests/KikiMAIster.Core.Tests --nologo -v q | grep -E "Passed!|Failed!"
out="dist/$branch/KikiMAIster"
rm -rf "$out" && mkdir -p "$out"
cp "build/$branch/KikiMAIster.dll" "build/$branch/KikiMAIster.json" "$out/"
echo "Ready to install: $out"
