#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCK_DIR="${SCRIPT_DIR}/site/.lunet/.build-lock"

have_rg() {
    command -v rg >/dev/null 2>&1
}

search_file_regex() {
    local pattern="$1"
    local file_path="$2"

    if have_rg; then
        rg -n -e "$pattern" "$file_path"
    else
        grep -nE -- "$pattern" "$file_path"
    fi
}

clean_docs_outputs() {
    find "${SCRIPT_DIR}/src" -path '*/obj/Release/*/*.api.json' -delete
    rm -rf "${SCRIPT_DIR}/site/.lunet/build/cache/api/dotnet" \
           "${SCRIPT_DIR}/site/.lunet/build/www"
}

cd "${SCRIPT_DIR}"
while ! mkdir "${LOCK_DIR}" 2>/dev/null; do
    sleep 1
done
trap 'rmdir "${LOCK_DIR}" 2>/dev/null || true' EXIT

dotnet tool restore
dotnet build "${SCRIPT_DIR}/XamlToCSharpGenerator.CI.slnf" -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
clean_docs_outputs

cd site
LUNET_LOG="$(mktemp)"
trap 'rm -f "${LUNET_LOG}"; rmdir "${LOCK_DIR}" 2>/dev/null || true' EXIT

dotnet tool run lunet --stacktrace build 2>&1 | tee "${LUNET_LOG}"

if search_file_regex 'ERR lunet|Error while building api dotnet|Unable to select the api dotnet output' "${LUNET_LOG}" >/dev/null; then
    echo "Lunet reported API/site build errors."
    exit 1
fi
