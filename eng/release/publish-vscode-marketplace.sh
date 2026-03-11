#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <vsix-path> [is-prerelease]" >&2
  exit 1
fi

vsix_path="$1"
is_prerelease="${2:-false}"

if [[ ! -f "${vsix_path}" ]]; then
  echo "VSIX package not found: ${vsix_path}" >&2
  exit 1
fi

if [[ -z "${VSCE_PAT:-}" ]]; then
  echo "VSCE_PAT is not configured. Skipping VS Code Marketplace publish." >&2
  exit 0
fi

cmd=(
  npx
  @vscode/vsce
  publish
  --packagePath
  "${vsix_path}"
  --pat
  "${VSCE_PAT}"
  --skip-duplicate
)

if [[ "${is_prerelease}" == "true" ]]; then
  cmd+=(--pre-release)
fi

set +e
output="$("${cmd[@]}" 2>&1)"
status=$?
set -e

if [[ $status -ne 0 ]]; then
  printf '%s\n' "${output}" >&2
  if [[ "${output}" == *"Failed request: (401)"* || "${output}" == *"TF400813"* || "${output}" == *"not authorized"* ]]; then
    echo "VS Code Marketplace authentication failed." >&2
    echo "Rotate VSCE_PAT and ensure it has publisher access for 'xamltocsharpgenerator' with Marketplace Manage permissions." >&2
  fi
  exit $status
fi

printf '%s\n' "${output}"
