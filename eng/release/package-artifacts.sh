#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <version> [output-root]" >&2
  exit 1
fi

version="$1"
output_root="${2:-./artifacts}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
nuget_dir="${output_root%/}/nuget"
vsix_path="${output_root%/}/vsix/axsg-language-server-${version}.vsix"

bash "${script_dir}/pack-nuget-artifacts.sh" "${version}" "${nuget_dir}"
bash "${script_dir}/package-vscode-extension.sh" "${version}" "${vsix_path}"

echo "Packaged NuGet artifacts to ${nuget_dir}"
echo "Packaged VS Code extension to ${vsix_path}"
