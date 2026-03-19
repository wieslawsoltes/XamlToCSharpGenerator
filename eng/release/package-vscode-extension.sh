#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <version> [output-vsix-path]" >&2
  exit 1
fi

if [[ $# -eq 2 && ( "$1" == */* || "$1" == .* || "$1" == ~* || "$1" == *.vsix ) ]]; then
  # Backward-compatible form: <output-vsix-path> <version>
  output_vsix="$1"
  version="$2"
else
  version="$1"
  output_vsix="${2:-./artifacts/vsix/axsg-language-server-${version}.vsix}"
fi
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_vsix="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "${output_vsix}")"
extension_dir="${repo_root}/tools/vscode/axsg-language-server"
package_json="${extension_dir}/package.json"
vscode_version="$(node "${repo_root}/eng/release/resolve-vscode-extension-version.mjs" "${version}")"
backup_file="$(mktemp)"
prerelease_mode="${AXSG_VSCODE_PRERELEASE:-auto}"

cp "${package_json}" "${backup_file}"
restore_package_json() {
  cp "${backup_file}" "${package_json}"
  rm -f "${backup_file}"
}
trap restore_package_json EXIT

mkdir -p "$(dirname "${output_vsix}")"

python3 - "${package_json}" "${vscode_version}" <<'PY'
import json
import sys
from pathlib import Path

package_json = Path(sys.argv[1])
version = sys.argv[2]
data = json.loads(package_json.read_text())
data["version"] = version
package_json.write_text(json.dumps(data, indent=2) + "\n")
PY

pushd "${extension_dir}" >/dev/null
echo "Packaging VS Code extension release ${version} as Marketplace version ${vscode_version}"
npm ci
vsce_args=(
  @vscode/vsce
  package
  --out
  "${output_vsix}"
)

if [[ "${prerelease_mode}" == "auto" ]]; then
  if [[ "${version}" == *-* ]]; then
    vsce_args+=(--pre-release)
  fi
elif [[ "${prerelease_mode}" == "true" ]]; then
  vsce_args+=(--pre-release)
elif [[ "${prerelease_mode}" != "false" ]]; then
  echo "Unsupported AXSG_VSCODE_PRERELEASE value: ${prerelease_mode}" >&2
  exit 1
fi

npx "${vsce_args[@]}"
popd >/dev/null
