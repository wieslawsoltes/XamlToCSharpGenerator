#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${SCRIPT_DIR}/build-docs.sh"

DOC_ROOT="${SCRIPT_DIR}/site/.lunet/build/www"

test -f "${DOC_ROOT}/index.html"
test -f "${DOC_ROOT}/api/index.html"
test -f "${DOC_ROOT}/articles/reference/index.html"
test -f "${DOC_ROOT}/articles/reference/package-guides/index.html"
test -f "${DOC_ROOT}/articles/reference/package-and-assembly/index.html"
test -f "${DOC_ROOT}/articles/reference/assembly-catalog/index.html"
test -f "${DOC_ROOT}/articles/reference/api-navigation-guide/index.html"
test -f "${DOC_ROOT}/articles/reference/feature-coverage-matrix/index.html"
test -f "${DOC_ROOT}/articles/reference/license/index.html"
test -f "${DOC_ROOT}/articles/concepts/glossary/index.html"
test -f "${DOC_ROOT}/articles/getting-started/samples-and-feature-tour/index.html"
test -f "${DOC_ROOT}/articles/guides/package-selection-and-integration/index.html"
test -f "${DOC_ROOT}/articles/guides/vscode-language-service/index.html"
test -f "${DOC_ROOT}/articles/guides/navigation-and-refactorings/index.html"
test -f "${DOC_ROOT}/articles/guides/runtime-loader-and-fallback/index.html"
test -f "${DOC_ROOT}/articles/guides/hot-reload-and-hot-design/index.html"
test -f "${DOC_ROOT}/articles/guides/troubleshooting/index.html"
test -f "${DOC_ROOT}/articles/xaml/event-bindings/index.html"
test -f "${DOC_ROOT}/articles/xaml/index.html"
test -f "${DOC_ROOT}/articles/xaml/conditional-xaml/index.html"
test -f "${DOC_ROOT}/articles/xaml/resources-includes-and-uris/index.html"
test -f "${DOC_ROOT}/articles/xaml/property-elements-templatebinding-and-attached-properties/index.html"
test -f "${DOC_ROOT}/articles/xaml/global-xmlns-and-project-configuration/index.html"
test -f "${DOC_ROOT}/articles/advanced/compiler-configuration-and-transform-rules/index.html"
test -f "${DOC_ROOT}/articles/advanced/language-service-and-compiler-performance/index.html"
test -f "${DOC_ROOT}/articles/advanced/hot-reload-and-hot-design/index.html"
test -f "${DOC_ROOT}/css/lite.css"

PACKAGE_GUIDE_PAGES=(
    xamltocsharpgenerator
    build
    compiler
    core
    framework-abstractions
    avalonia
    expression-semantics
    mini-language-parsing
    noui
    generator
    runtime
    runtime-core
    runtime-avalonia
    language-service
    language-server-tool
    editor-avalonia
    vscode-extension
)

for package_name in "${PACKAGE_GUIDE_PAGES[@]}"; do
    test -f "${DOC_ROOT}/articles/reference/${package_name}/index.html"
done

if test -e "${DOC_ROOT}/api/System.Runtime.CompilerServices/index.html"; then
    echo "Generated docs unexpectedly expose System.Runtime.CompilerServices namespace."
    exit 1
fi

if rg -n 'href="[^"]*\.md"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain raw .md links."
    exit 1
fi

if rg -n 'href="[^"]*/readme(?:[?#"][^"]*)?"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain /readme routes instead of directory routes."
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

EDITOR_API_PAGE="${DOC_ROOT}/api/XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor/index.html"
if ! test -f "${EDITOR_API_PAGE}"; then
    echo "Expected editor API page is missing: ${EDITOR_API_PAGE}"
    exit 1
fi

if ! rg -F 'https://api-docs.avaloniaui.net/docs/AvaloniaEdit.TextEditor/' "${EDITOR_API_PAGE}" >/dev/null; then
    echo "Generated editor API page is missing the external AvaloniaEdit.TextEditor link."
    exit 1
fi

XAML_INDEX_PAGE="${DOC_ROOT}/articles/xaml/index.html"
if ! rg -F '/XamlToCSharpGenerator/css/lite.css' "${XAML_INDEX_PAGE}" >/dev/null; then
    echo "Production XAML docs page is missing the project-basepath-prefixed lite.css URL."
    exit 1
fi

if ! rg -F "/XamlToCSharpGenerator/partials/menus/menu-xaml." "${XAML_INDEX_PAGE}" >/dev/null; then
    echo "Production XAML docs page is missing the project-basepath-prefixed async menu partial URL."
    exit 1
fi
