#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <output-vsix-path> <version>" >&2
  exit 1
fi

output_vsix="$1"
version="$2"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_vsix="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "${output_vsix}")"
extension_dir="${repo_root}/tools/vscode/axsg-language-server"
package_json="${extension_dir}/package.json"
backup_file="$(mktemp)"

cp "${package_json}" "${backup_file}"
restore_package_json() {
  cp "${backup_file}" "${package_json}"
  rm -f "${backup_file}"
}
trap restore_package_json EXIT

mkdir -p "$(dirname "${output_vsix}")"

python3 - "${package_json}" "${version}" <<'PY'
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
npm ci
npx @vscode/vsce package --out "${output_vsix}"
popd >/dev/null
