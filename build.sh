#!/usr/bin/env bash
# Builds the mod and puts the files to install in dist/<branch>/KikiMAIster/
# Usage: ./build.sh [beta|default] [release]   (default: beta)
# "release": public build (F9 opens the GitHub issue form) zipped as dist/release/KikiMAIster-<version>-<branch>.zip
set -euo pipefail
branch="${1:-beta}"
release="${2:-}"
public=false; [ "$release" = "release" ] && public=true
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_ROOT=~/.dotnet PATH=$PATH:~/.dotnet:~/.dotnet/tools
cd "$(dirname "$0")"
dotnet build src/KikiMAIster -c Release -p:GameBranch="$branch" -p:PublicRelease=$public -o "build/$branch" --nologo -v q
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
if [ "$public" = true ]; then
  version=$(grep -o '"version": *"[^"]*"' "$out/KikiMAIster.json" | grep -o '[0-9][^"]*')
  mkdir -p dist/release
  zip="dist/release/KikiMAIster-$version-$branch.zip"
  rm -f "$zip" && (cd "dist/$branch" && python3 -c "import shutil; shutil.make_archive('../release/KikiMAIster-$version-$branch', 'zip', '.', 'KikiMAIster')")
  echo "Release zip: $zip"
fi
