#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <version> [output-dir]" >&2
  exit 1
fi

if [[ $# -eq 2 && ( "$1" == */* || "$1" == .* || "$1" == ~* ) ]]; then
  # Backward-compatible form: <output-dir> <version>
  output_dir="$1"
  version="$2"
else
  version="$1"
  output_dir="${2:-./artifacts/nuget}"
fi

mkdir -p "${output_dir}"

projects=(
  "src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj"
  "src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj"
  "src/XamlToCSharpGenerator.Build/XamlToCSharpGenerator.Build.csproj"
  "src/XamlToCSharpGenerator.Compiler/XamlToCSharpGenerator.Compiler.csproj"
  "src/XamlToCSharpGenerator.Core/XamlToCSharpGenerator.Core.csproj"
  "src/XamlToCSharpGenerator.Editor.Avalonia/XamlToCSharpGenerator.Editor.Avalonia.csproj"
  "src/XamlToCSharpGenerator.ExpressionSemantics/XamlToCSharpGenerator.ExpressionSemantics.csproj"
  "src/XamlToCSharpGenerator.Framework.Abstractions/XamlToCSharpGenerator.Framework.Abstractions.csproj"
  "src/XamlToCSharpGenerator.Generator/XamlToCSharpGenerator.Generator.csproj"
  "src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj"
  "src/XamlToCSharpGenerator.LanguageService/XamlToCSharpGenerator.LanguageService.csproj"
  "src/XamlToCSharpGenerator.MiniLanguageParsing/XamlToCSharpGenerator.MiniLanguageParsing.csproj"
  "src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj"
  "src/XamlToCSharpGenerator.Runtime/XamlToCSharpGenerator.Runtime.csproj"
  "src/XamlToCSharpGenerator.Runtime.Avalonia/XamlToCSharpGenerator.Runtime.Avalonia.csproj"
  "src/XamlToCSharpGenerator.Runtime.Core/XamlToCSharpGenerator.Runtime.Core.csproj"
)

for project in "${projects[@]}"; do
  dotnet pack "${project}" \
    -c Release \
    -o "${output_dir}" \
    -m:1 \
    /nodeReuse:false \
    --disable-build-servers \
    /p:ContinuousIntegrationBuild=true \
    /p:Version="${version}"
done
