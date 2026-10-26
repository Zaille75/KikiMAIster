#!/usr/bin/env bash
# Builds the mod and puts the files to install in dist/<branch>/KikiMAIster/
# Usage: ./build.sh [beta|default]   (default: beta)
set -euo pipefail
branch="${1:-beta}"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_ROOT=~/.dotnet PATH=$PATH:~/.dotnet:~/.dotnet/tools
cd "$(dirname "$0")"
dotnet build src/KikiMAIster -c Release -p:GameBranch="$branch" -o "build/$branch" --nologo -v q
out="dist/$branch/KikiMAIster"
rm -rf "$out" && mkdir -p "$out"
cp "build/$branch/KikiMAIster.dll" "build/$branch/KikiMAIster.json" "$out/"
echo "Ready to install: $out"
