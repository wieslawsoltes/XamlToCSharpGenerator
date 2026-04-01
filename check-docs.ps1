$ErrorActionPreference = 'Stop'

$script:HasRipgrep = $null -ne (Get-Command rg -ErrorAction SilentlyContinue)

function Find-TreeRegex {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($script:HasRipgrep) {
        & rg -n -e $Pattern $Path
        return
    }

    Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue |
        Select-String -Pattern $Pattern |
        ForEach-Object {
            "{0}:{1}:{2}" -f $_.Path, $_.LineNumber, $_.Line.TrimEnd()
        }
}

function Test-FileFixedText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($script:HasRipgrep) {
        & rg -F -- $Text $Path *> $null
        return $LASTEXITCODE -eq 0
    }

    return $null -ne (Select-String -Path $Path -SimpleMatch -Pattern $Text -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Assert-SourceMarkdownLinks {
    $siteRoot = Join-Path $PSScriptRoot 'site'
    $sourceFiles = @((Join-Path $siteRoot 'readme.md')) + @(
        Get-ChildItem -Path (Join-Path $siteRoot 'articles') -Filter *.md -Recurse -File
    )

    $linkPattern = [regex]'(?<!!)\[[^\]]+\]\(([^)]+)\)'
    $brokenLinks = New-Object 'System.Collections.Generic.List[string]'

    foreach ($sourceFile in $sourceFiles) {
        $sourcePath = if ($sourceFile -is [string]) { $sourceFile } else { $sourceFile.FullName }
        $sourceText = Get-Content -Path $sourcePath -Raw
        $matches = $linkPattern.Matches($sourceText)

        foreach ($match in $matches) {
            $url = $match.Groups[1].Value.Trim()
            if (
                [string]::IsNullOrWhiteSpace($url) -or
                $url.StartsWith('#') -or
                $url.StartsWith('http://') -or
                $url.StartsWith('https://') -or
                $url.StartsWith('mailto:') -or
                $url.StartsWith('tel:') -or
                $url.StartsWith('javascript:') -or
                $url.StartsWith('xref:') -or
                $url.StartsWith('<xref:') -or
                $url -eq '/api' -or
                $url.StartsWith('/api/') -or
                $url -eq 'api' -or
                $url.StartsWith('api/') -or
                $url.StartsWith('/images/') -or
                $url.StartsWith('/css/') -or
                $url.StartsWith('/js/') -or
                $url.StartsWith('/fonts/') -or
                $url.StartsWith('/modules/') -or
                $url.StartsWith('/partials/')
            ) {
                continue
            }

            $pathPart = ($url -replace '[?#].*$', '').TrimEnd('/', '\')
            if ([string]::IsNullOrWhiteSpace($pathPart)) {
                continue
            }

            if ([System.IO.Path]::IsPathRooted($pathPart) -or $pathPart.StartsWith('/')) {
                $candidate = Join-Path $siteRoot $pathPart.TrimStart('/')
            }
            else {
                $candidate = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Path $sourcePath -Parent) $pathPart))
            }

            if (
                (Test-Path -Path $candidate) -or
                (Test-Path -Path ($candidate + '.md')) -or
                (Test-Path -Path (Join-Path $candidate 'readme.md'))
            ) {
                continue
            }

            $lineNumber = ($sourceText.Substring(0, $match.Index) -split "`n").Count
            $displayPath = $sourcePath.Replace(($PSScriptRoot + [System.IO.Path]::DirectorySeparatorChar), '')
            $brokenLinks.Add("${displayPath}:${lineNumber}: $url")
        }
    }

    if ($brokenLinks.Count -gt 0) {
        $issues = $brokenLinks -join "`n"
        throw "Source docs contain broken internal Markdown links.`n$issues"
    }
}

Assert-SourceMarkdownLinks
& (Join-Path $PSScriptRoot 'build-docs.ps1')

$docRoot = Join-Path $PSScriptRoot 'site/.lunet/build/www'

$requiredFiles = @(
    (Join-Path $docRoot 'index.html'),
    (Join-Path $docRoot 'api/index.html'),
    (Join-Path $docRoot 'articles/reference/index.html'),
    (Join-Path $docRoot 'articles/reference/package-guides/index.html'),
    (Join-Path $docRoot 'articles/reference/package-and-assembly/index.html'),
    (Join-Path $docRoot 'articles/reference/assembly-catalog/index.html'),
    (Join-Path $docRoot 'articles/reference/api-navigation-guide/index.html'),
    (Join-Path $docRoot 'articles/reference/feature-coverage-matrix/index.html'),
    (Join-Path $docRoot 'articles/reference/license/index.html'),
    (Join-Path $docRoot 'articles/concepts/glossary/index.html'),
    (Join-Path $docRoot 'articles/getting-started/samples-and-feature-tour/index.html'),
    (Join-Path $docRoot 'articles/guides/package-selection-and-integration/index.html'),
    (Join-Path $docRoot 'articles/guides/vscode-language-service/index.html'),
    (Join-Path $docRoot 'articles/guides/navigation-and-refactorings/index.html'),
    (Join-Path $docRoot 'articles/guides/runtime-loader-and-fallback/index.html'),
    (Join-Path $docRoot 'articles/guides/hot-reload-and-hot-design/index.html'),
    (Join-Path $docRoot 'articles/guides/troubleshooting/index.html'),
    (Join-Path $docRoot 'articles/xaml/index.html'),
    (Join-Path $docRoot 'articles/xaml/event-bindings/index.html'),
    (Join-Path $docRoot 'articles/xaml/conditional-xaml/index.html'),
    (Join-Path $docRoot 'articles/xaml/resources-includes-and-uris/index.html'),
    (Join-Path $docRoot 'articles/xaml/property-elements-templatebinding-and-attached-properties/index.html'),
    (Join-Path $docRoot 'articles/xaml/global-xmlns-and-project-configuration/index.html'),
    (Join-Path $docRoot 'articles/advanced/compiler-configuration-and-transform-rules/index.html'),
    (Join-Path $docRoot 'articles/advanced/language-service-and-compiler-performance/index.html'),
    (Join-Path $docRoot 'articles/advanced/hot-reload-and-hot-design/index.html'),
    (Join-Path $docRoot 'css/lite.css')
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Required docs output missing: $file"
    }
}

$packagePages = @(
    'xamltocsharpgenerator',
    'build',
    'compiler',
    'core',
    'framework-abstractions',
    'avalonia',
    'expression-semantics',
    'mini-language-parsing',
    'noui',
    'generator',
    'runtime',
    'runtime-core',
    'runtime-avalonia',
    'remote-protocol',
    'language-service',
    'language-server-tool',
    'mcp-server-tool',
    'editor-avalonia',
    'vscode-extension'
)

foreach ($packagePage in $packagePages) {
    $outputPage = Join-Path $docRoot ("articles/reference/" + $packagePage + "/index.html")
    if (-not (Test-Path $outputPage)) {
        throw "Generated package guide output missing: $outputPage"
    }
}

$rawMarkdownLinks = Find-TreeRegex -Pattern 'href="[^"]*\.md"' -Path $docRoot
if ($rawMarkdownLinks) {
    throw "Generated docs contain raw .md links.`n$rawMarkdownLinks"
}

$readmeRoutes = Find-TreeRegex -Pattern 'href="[^"]*/readme(?:[?#"][^"]*)?"' -Path $docRoot
if ($readmeRoutes) {
    throw "Generated docs contain /readme routes instead of directory routes.`n$readmeRoutes"
}

$stalePackageRoutes = Find-TreeRegex -Pattern 'href="[^"]*/articles/reference/packages(?:/|["?#])' -Path $docRoot
if ($stalePackageRoutes) {
    throw "Generated docs contain stale /articles/reference/packages routes.`n$stalePackageRoutes"
}

$rawMarkdownOutputs = Get-ChildItem -Path (Join-Path $docRoot 'articles') -Filter *.md -Recurse -ErrorAction SilentlyContinue
if ($rawMarkdownOutputs.Count -gt 0) {
    $paths = ($rawMarkdownOutputs | ForEach-Object { $_.FullName }) -join "`n"
    throw "Generated docs still contain raw .md article outputs.`n$paths"
}

$compilerServicesIndex = Join-Path $docRoot 'api/System.Runtime.CompilerServices/index.html'
if (Test-Path $compilerServicesIndex) {
    throw "Generated docs unexpectedly expose System.Runtime.CompilerServices namespace."
}

$badFooterText = Find-TreeRegex -Pattern 'Creative Commons <a href="https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/LICENSE">MIT</a>|Creative Commons MIT' -Path $docRoot
if ($badFooterText) {
    throw "Generated docs contain incorrect Creative Commons MIT footer text.`n$badFooterText"
}

$editorApiPage = Join-Path $docRoot 'api/XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor/index.html'
if (-not (Test-Path $editorApiPage)) {
    throw "Expected editor API page is missing: $editorApiPage"
}

if (-not (Test-FileFixedText -Text 'https://api-docs.avaloniaui.net/docs/AvaloniaEdit.TextEditor/' -Path $editorApiPage)) {
    throw "Generated editor API page is missing the external AvaloniaEdit.TextEditor link."
}

$xamlIndexPage = Join-Path $docRoot 'articles/xaml/index.html'
if (-not (Test-FileFixedText -Text '/XamlToCSharpGenerator/css/lite.css' -Path $xamlIndexPage)) {
    throw "Production XAML docs page is missing the project-basepath-prefixed lite.css URL."
}

if (-not (Test-FileFixedText -Text '/XamlToCSharpGenerator/partials/menus/menu-xaml.' -Path $xamlIndexPage)) {
    throw "Production XAML docs page is missing the project-basepath-prefixed async menu partial URL."
}
