#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCK_DIR="${SCRIPT_DIR}/site/.lunet/.build-lock"

cd "${SCRIPT_DIR}"
while ! mkdir "${LOCK_DIR}" 2>/dev/null; do
    sleep 1
done
trap 'rmdir "${LOCK_DIR}" 2>/dev/null || true' EXIT

dotnet tool restore
dotnet build "${SCRIPT_DIR}/XamlToCSharpGenerator.CI.slnf" -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers

# Lunet caches generated api.json files aggressively. Clear API-specific outputs so
# docs reflect current project configuration and namespace visibility.
find "${SCRIPT_DIR}/src" -path '*/obj/Release/*/*.api.json' -delete
rm -rf "${SCRIPT_DIR}/site/.lunet/build/cache/api/dotnet" \
       "${SCRIPT_DIR}/site/.lunet/build/www/api" \
       "${SCRIPT_DIR}/site/.lunet/build/www/partials/menus"

cd site
LUNET_LOG="$(mktemp)"
trap 'rm -f "${LUNET_LOG}"; rmdir "${LOCK_DIR}" 2>/dev/null || true' EXIT

dotnet tool run lunet --stacktrace build 2>&1 | tee "${LUNET_LOG}"

if rg -n 'ERR lunet|Error while building api dotnet|Unable to select the api dotnet output' "${LUNET_LOG}" >/dev/null; then
    echo "Lunet reported API/site build errors."
    exit 1
fi
