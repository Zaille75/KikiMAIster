#!/usr/bin/env bash
# Builds the mod (one DLL for both game branches, v1.1.0) and puts the files to install in dist/KikiMAIster/
# Usage: ./build.sh [release]
# "release": public build (F9 opens the GitHub issue form) zipped as dist/release/KikiMAIster-<version>.zip
set -euo pipefail
release="${1:-}"
public=false; [ "$release" = "release" ] && public=true
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_ROOT=~/.dotnet PATH=$PATH:~/.dotnet:~/.dotnet/tools
cd "$(dirname "$0")"
dotnet build src/KikiMAIster -c Release -p:PublicRelease=$public -o build/mod --nologo -v q
# On each game branch: every active Harmony patch matches exactly one game method, and every game member the DLL uses
# exists (catches breakage after game updates, and main branch differences).
for branch in beta default; do
  [ -f "lib/$branch/sts2.dll" ] || { echo "lib/$branch: no game DLLs, skipped"; continue; }
  check=$(dotnet run --project tools/PatchCheck -v q -- "lib/$branch" "build/mod/KikiMAIster.dll")
  echo "$branch: $(echo "$check" | tail -1)"
  if echo "$check" | grep -q '^FAIL'; then
    echo "$check" | grep '^FAIL'; echo "Patch check failed for branch $branch" >&2; exit 1
  fi
done
dotnet test tests/KikiMAIster.Core.Tests --nologo -v q | grep -E "Passed!|Failed!"
out="dist/KikiMAIster"
rm -rf "$out" && mkdir -p "$out"
cp "build/mod/KikiMAIster.dll" "build/mod/KikiMAIster.json" "$out/"
echo "Ready to install: $out"
if [ "$public" = true ]; then
  version=$(grep -o '"version": *"[^"]*"' "$out/KikiMAIster.json" | grep -o '[0-9][^"]*')
  mkdir -p dist/release
  zip="dist/release/KikiMAIster-$version.zip"
  rm -f "$zip" && (cd dist && python3 -c "import shutil; shutil.make_archive('release/KikiMAIster-$version', 'zip', '.', 'KikiMAIster')")
  echo "Release zip: $zip"
fi
