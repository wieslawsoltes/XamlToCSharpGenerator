#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${SCRIPT_DIR}/build-docs.sh"

DOC_ROOT="${SCRIPT_DIR}/site/.lunet/build/www"

test -f "${DOC_ROOT}/index.html"
test -f "${DOC_ROOT}/api/index.html"
test -f "${DOC_ROOT}/articles/reference/index.html"
test -f "${DOC_ROOT}/articles/reference/packages/index.html"
test -f "${DOC_ROOT}/articles/reference/package-and-assembly/index.html"
test -f "${DOC_ROOT}/articles/reference/assembly-catalog/index.html"
test -f "${DOC_ROOT}/articles/reference/api-navigation-guide/index.html"
test -f "${DOC_ROOT}/articles/reference/feature-coverage-matrix/index.html"
test -f "${DOC_ROOT}/articles/reference/license/index.html"
test -f "${DOC_ROOT}/articles/getting-started/samples-and-feature-tour/index.html"
test -f "${DOC_ROOT}/articles/guides/package-selection-and-integration/index.html"
test -f "${DOC_ROOT}/articles/guides/vscode-language-service/index.html"
test -f "${DOC_ROOT}/articles/guides/navigation-and-refactorings/index.html"
test -f "${DOC_ROOT}/articles/guides/runtime-loader-and-fallback/index.html"
test -f "${DOC_ROOT}/articles/guides/hot-reload-and-hot-design/index.html"
test -f "${DOC_ROOT}/articles/xaml/event-bindings/index.html"
test -f "${DOC_ROOT}/articles/xaml/resources-includes-and-uris/index.html"
test -f "${DOC_ROOT}/articles/xaml/property-elements-templatebinding-and-attached-properties/index.html"
test -f "${DOC_ROOT}/articles/xaml/global-xmlns-and-project-configuration/index.html"
test -f "${DOC_ROOT}/articles/advanced/compiler-configuration-and-transform-rules/index.html"
test -f "${DOC_ROOT}/articles/advanced/language-service-and-compiler-performance/index.html"
test -f "${DOC_ROOT}/articles/advanced/hot-reload-and-hot-design/index.html"
test -f "${DOC_ROOT}/css/lite.css"

while IFS= read -r package_page; do
    package_name="$(basename "${package_page}" .md)"
    test -f "${DOC_ROOT}/articles/reference/packages/${package_name}/index.html"
done < <(find "${SCRIPT_DIR}/site/articles/reference/packages" -maxdepth 1 -type f -name '*.md' ! -name 'menu.yml' ! -name 'readme.md' | sort)

if test -e "${DOC_ROOT}/api/System.Runtime.CompilerServices/index.html"; then
    echo "Generated docs unexpectedly expose System.Runtime.CompilerServices namespace."
    exit 1
fi

if rg -n 'href="[^"]*\.md"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain raw .md links."
    exit 1
fi

if find "${DOC_ROOT}/articles" -name '*.md' -print -quit | grep -q .; then
    echo "Generated docs still contain raw .md article outputs."
    find "${DOC_ROOT}/articles" -name '*.md' -print
    exit 1
fi

if rg -n 'Creative Commons <a href="https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/LICENSE">MIT</a>|Creative Commons MIT' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain incorrect Creative Commons MIT footer text."
    exit 1
fi
