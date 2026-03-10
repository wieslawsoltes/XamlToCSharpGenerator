$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-docs.ps1')

$docRoot = Join-Path $PSScriptRoot 'site/.lunet/build/www'

$requiredFiles = @(
    (Join-Path $docRoot 'index.html'),
    (Join-Path $docRoot 'api/index.html'),
    (Join-Path $docRoot 'articles/reference/index.html'),
    (Join-Path $docRoot 'articles/reference/packages/index.html'),
    (Join-Path $docRoot 'articles/reference/package-and-assembly/index.html'),
    (Join-Path $docRoot 'articles/reference/license/index.html'),
    (Join-Path $docRoot 'css/lite.css')
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Required docs output missing: $file"
    }
}

$rawMarkdownLinks = rg -n 'href="[^"]*\.md"' $docRoot
if ($LASTEXITCODE -eq 0 -and $rawMarkdownLinks) {
    throw "Generated docs contain raw .md links.`n$rawMarkdownLinks"
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
