#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

have_rg() {
    command -v rg >/dev/null 2>&1
}

search_tree_regex() {
    local pattern="$1"
    local root_path="$2"

    if have_rg; then
        rg -n -e "$pattern" "$root_path"
    else
        grep -RInE -- "$pattern" "$root_path"
    fi
}

search_file_fixed() {
    local text="$1"
    local file_path="$2"

    if have_rg; then
        rg -F -- "$text" "$file_path"
    else
        grep -F -- "$text" "$file_path"
    fi
}

normalize_source_path() {
    local target="$1"
    local base_dir="$2"
    perl -MFile::Spec -e 'print File::Spec->canonpath(File::Spec->rel2abs($ARGV[0], $ARGV[1]))' "$target" "$base_dir"
}

check_source_markdown_links() {
    local file_path line_number url path_part candidate source_display
    local -a broken_links=()

    while IFS= read -r -d '' file_path; do
        while IFS=$'\t' read -r line_number url; do
            if [[ -z "$url" || "$url" == \#* || "$url" == http://* || "$url" == https://* || "$url" == mailto:* || "$url" == tel:* || "$url" == javascript:* || "$url" == xref:* || "$url" == \<xref:* || "$url" == /api || "$url" == /api/* || "$url" == api || "$url" == api/* || "$url" == /images/* || "$url" == /css/* || "$url" == /js/* || "$url" == /fonts/* || "$url" == /modules/* || "$url" == /partials/* ]]; then
                continue
            fi

            path_part="${url%%[?#]*}"
            if [[ -z "$path_part" ]]; then
                continue
            fi

            if [[ "$path_part" == /* ]]; then
                candidate="${SCRIPT_DIR}/site/${path_part#/}"
            else
                candidate="$(normalize_source_path "$path_part" "$(dirname "$file_path")")"
            fi

            if [[ -e "$candidate" || -e "${candidate}.md" || -e "${candidate}/readme.md" ]]; then
                continue
            fi

            source_display="${file_path#${SCRIPT_DIR}/}"
            broken_links+=("${source_display}:${line_number}: ${url}")
        done < <(perl -ne 'while (/(?<!!)\[[^\]]+\]\(([^)]+)\)/g) { print $.,"\t",$1,"\n"; }' "$file_path")
    done < <(
        printf '%s\0' "${SCRIPT_DIR}/site/readme.md"
        find "${SCRIPT_DIR}/site/articles" -name '*.md' -print0
    )

    if ((${#broken_links[@]} > 0)); then
        echo "Source docs contain broken internal Markdown links." >&2
        printf '%s\n' "${broken_links[@]}" >&2
        exit 1
    fi
}

check_source_markdown_links
"${SCRIPT_DIR}/build-docs.sh"

DOC_ROOT="${SCRIPT_DIR}/site/.lunet/build/www"

test -f "${DOC_ROOT}/index.html"
test -f "${DOC_ROOT}/api/index.html"
test -f "${DOC_ROOT}/articles/reference/index.html"
test -f "${DOC_ROOT}/articles/reference/package-guides/index.html"
test -f "${DOC_ROOT}/articles/reference/package-and-assembly/index.html"
test -f "${DOC_ROOT}/articles/reference/assembly-catalog/index.html"
test -f "${DOC_ROOT}/articles/reference/preview-host/index.html"
test -f "${DOC_ROOT}/articles/reference/api-navigation-guide/index.html"
test -f "${DOC_ROOT}/articles/reference/feature-coverage-matrix/index.html"
test -f "${DOC_ROOT}/articles/reference/license/index.html"
test -f "${DOC_ROOT}/articles/concepts/glossary/index.html"
test -f "${DOC_ROOT}/articles/getting-started/samples-and-feature-tour/index.html"
test -f "${DOC_ROOT}/articles/guides/package-selection-and-integration/index.html"
test -f "${DOC_ROOT}/articles/guides/vscode-language-service/index.html"
test -f "${DOC_ROOT}/articles/guides/preview-mcp-host-and-live-preview/index.html"
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
    remote-protocol
    language-service
    language-server-tool
    mcp-server-tool
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

if search_tree_regex 'href="[^"]*\.md"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain raw .md links."
    exit 1
fi

if search_tree_regex 'href="[^"]*/readme(?:[?#"][^"]*)?"' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain /readme routes instead of directory routes."
    exit 1
fi

if search_tree_regex 'href="[^"]*/articles/reference/packages(?:/|["?#])' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain stale /articles/reference/packages routes."
    exit 1
fi

if find "${DOC_ROOT}/articles" -name '*.md' -print -quit | grep -q .; then
    echo "Generated docs still contain raw .md article outputs."
    find "${DOC_ROOT}/articles" -name '*.md' -print
    exit 1
fi

if search_tree_regex 'Creative Commons <a href="https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/LICENSE">MIT</a>|Creative Commons MIT' "${DOC_ROOT}" >/dev/null; then
    echo "Generated docs contain incorrect Creative Commons MIT footer text."
    exit 1
fi

EDITOR_API_PAGE="${DOC_ROOT}/api/XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor/index.html"
if ! test -f "${EDITOR_API_PAGE}"; then
    echo "Expected editor API page is missing: ${EDITOR_API_PAGE}"
    exit 1
fi

if ! search_file_fixed 'https://api-docs.avaloniaui.net/docs/AvaloniaEdit.TextEditor/' "${EDITOR_API_PAGE}" >/dev/null; then
    echo "Generated editor API page is missing the external AvaloniaEdit.TextEditor link."
    exit 1
fi

XAML_INDEX_PAGE="${DOC_ROOT}/articles/xaml/index.html"
if ! search_file_fixed '/XamlToCSharpGenerator/css/lite.css' "${XAML_INDEX_PAGE}" >/dev/null; then
    echo "Production XAML docs page is missing the project-basepath-prefixed lite.css URL."
    exit 1
fi

if ! search_file_fixed "/XamlToCSharpGenerator/partials/menus/menu-xaml." "${XAML_INDEX_PAGE}" >/dev/null; then
    echo "Production XAML docs page is missing the project-basepath-prefixed async menu partial URL."
    exit 1
fi
