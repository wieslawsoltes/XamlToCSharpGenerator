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

"${cmd[@]}"
