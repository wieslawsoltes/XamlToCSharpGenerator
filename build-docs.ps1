$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    dotnet tool restore
    dotnet build (Join-Path $PSScriptRoot 'XamlToCSharpGenerator.CI.slnf') -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers

    # Lunet caches generated api.json files aggressively. Clear API-specific outputs so
    # docs reflect current project configuration and namespace visibility.
    Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.api.json' -Recurse -File |
        Where-Object { $_.FullName -like '*\obj\Release\*' } |
        Remove-Item -Force

    $apiCache = Join-Path $PSScriptRoot 'site/.lunet/build/cache/api/dotnet'
    $apiOutput = Join-Path $PSScriptRoot 'site/.lunet/build/www/api'
    $menuOutput = Join-Path $PSScriptRoot 'site/.lunet/build/www/partials/menus'
    foreach ($path in @($apiCache, $apiOutput, $menuOutput)) {
        Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
    }

    Push-Location site
    try {
        dotnet tool run lunet --stacktrace build
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}
