#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cd "${SCRIPT_DIR}"
dotnet tool restore
dotnet build "${SCRIPT_DIR}/XamlToCSharpGenerator.CI.slnf" -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers

# Lunet caches generated api.json files aggressively. Clear API-specific outputs so
# docs reflect current project configuration and namespace visibility.
find "${SCRIPT_DIR}/src" -path '*/obj/Release/*/*.api.json' -delete
rm -rf "${SCRIPT_DIR}/site/.lunet/build/cache/api/dotnet" \
       "${SCRIPT_DIR}/site/.lunet/build/www/api" \
       "${SCRIPT_DIR}/site/.lunet/build/www/partials/menus"

cd site
dotnet tool run lunet --stacktrace build
