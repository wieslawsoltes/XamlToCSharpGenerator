$ErrorActionPreference = 'Stop'
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
    'language-service',
    'language-server-tool',
    'editor-avalonia',
    'vscode-extension'
)

foreach ($packagePage in $packagePages) {
    $outputPage = Join-Path $docRoot ("articles/reference/" + $packagePage + "/index.html")
    if (-not (Test-Path $outputPage)) {
        throw "Generated package guide output missing: $outputPage"
    }
}

$rawMarkdownLinks = rg -n 'href="[^"]*\.md"' $docRoot
if ($LASTEXITCODE -eq 0 -and $rawMarkdownLinks) {
    throw "Generated docs contain raw .md links.`n$rawMarkdownLinks"
}

$readmeRoutes = rg -n 'href="[^"]*/readme(?:[?#"][^"]*)?"' $docRoot
if ($LASTEXITCODE -eq 0 -and $readmeRoutes) {
    throw "Generated docs contain /readme routes instead of directory routes.`n$readmeRoutes"
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

$badFooterText = rg -n 'Creative Commons <a href="https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/LICENSE">MIT</a>|Creative Commons MIT' $docRoot
if ($LASTEXITCODE -eq 0 -and $badFooterText) {
    throw "Generated docs contain incorrect Creative Commons MIT footer text.`n$badFooterText"
}

$editorApiPage = Join-Path $docRoot 'api/XamlToCSharpGenerator.Editor.Avalonia.AxamlTextEditor/index.html'
if (-not (Test-Path $editorApiPage)) {
    throw "Expected editor API page is missing: $editorApiPage"
}

$missingAvaloniaEditLink = rg -F 'https://api-docs.avaloniaui.net/docs/AvaloniaEdit.TextEditor/' $editorApiPage
if ($LASTEXITCODE -ne 0) {
    throw "Generated editor API page is missing the external AvaloniaEdit.TextEditor link."
}

$xamlIndexPage = Join-Path $docRoot 'articles/xaml/index.html'
$missingBasepathCss = rg -F '/XamlToCSharpGenerator/css/lite.css' $xamlIndexPage
if ($LASTEXITCODE -ne 0) {
    throw "Production XAML docs page is missing the project-basepath-prefixed lite.css URL."
}

$missingMenuPartial = rg -F '/XamlToCSharpGenerator/partials/menus/menu-xaml.' $xamlIndexPage
if ($LASTEXITCODE -ne 0) {
    throw "Production XAML docs page is missing the project-basepath-prefixed async menu partial URL."
}
