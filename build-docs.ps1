$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    dotnet tool restore
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
