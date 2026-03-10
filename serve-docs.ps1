$ErrorActionPreference = 'Stop'
$hostAddress = if ($env:DOCS_HOST) { $env:DOCS_HOST } else { '127.0.0.1' }
$port = if ($env:DOCS_PORT) { $env:DOCS_PORT } else { '8080' }

function Clear-ServeDocsOutputs {
    $wwwRoot = Join-Path $PSScriptRoot 'site/.lunet/build/www'
    Remove-Item $wwwRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Push-Location $PSScriptRoot
try {
    dotnet tool restore
    Clear-ServeDocsOutputs
    Push-Location site
    try {
    $python = $null
    foreach ($candidate in @('python3', 'python', 'py')) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            $python = $candidate
            break
        }
    }

    if (-not $python) {
        Write-Warning "Python runtime not found (python3/python/py). Falling back to 'lunet serve'."
        dotnet tool run lunet --stacktrace serve
        return
    }

    dotnet tool run lunet --stacktrace build --dev
    $watchProcess = Start-Process dotnet -ArgumentList @('tool', 'run', 'lunet', '--stacktrace', 'build', '--dev', '--watch') -PassThru -NoNewWindow
    try {
        Write-Host "Serving docs at http://${hostAddress}:$port"
        Write-Host 'Watching docs with Lunet (dev mode)...'
        Push-Location '.lunet/build/www'
        try {
            & $python -m http.server $port --bind $hostAddress
        }
        finally {
            Pop-Location
        }
    }
    finally {
        if ($watchProcess -and -not $watchProcess.HasExited) {
            Stop-Process -Id $watchProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}
