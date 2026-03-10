#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST="${DOCS_HOST:-127.0.0.1}"
PORT="${DOCS_PORT:-8080}"

clean_docs_outputs() {
    find "${SCRIPT_DIR}/src" -path '*/obj/Release/*/*.api.json' -delete
    rm -rf "${SCRIPT_DIR}/site/.lunet/build/cache/api/dotnet" \
           "${SCRIPT_DIR}/site/.lunet/build/www"
}

cd "${SCRIPT_DIR}"
dotnet tool restore
dotnet build "${SCRIPT_DIR}/XamlToCSharpGenerator.CI.slnf" -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
clean_docs_outputs
cd site

if command -v python3 >/dev/null 2>&1; then
    PYTHON_BIN="python3"
elif command -v python >/dev/null 2>&1; then
    PYTHON_BIN="python"
else
    echo "Python runtime not found (python3/python). Falling back to 'lunet serve'." >&2
    dotnet tool run lunet --stacktrace serve
    exit 0
fi

dotnet tool run lunet --stacktrace build --dev

dotnet tool run lunet --stacktrace build --dev --watch &
LUNET_WATCH_PID=$!

cleanup() {
    kill "${LUNET_WATCH_PID}" >/dev/null 2>&1 || true
}

trap cleanup EXIT INT TERM

echo "Serving docs at http://${HOST}:${PORT}"
echo "Watching docs with Lunet (dev mode)..."

cd .lunet/build/www
"${PYTHON_BIN}" -m http.server "${PORT}" --bind "${HOST}"
