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
test -f "${DOC_ROOT}/articles/reference/license/index.html"
test -f "${DOC_ROOT}/css/lite.css"

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
