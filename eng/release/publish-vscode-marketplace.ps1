param(
    [Parameter(Mandatory = $true)]
    [string]$VsixPath,

    [Parameter(Mandatory = $false)]
    [bool]$IsPrerelease = $false
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $VsixPath)) {
    throw "VSIX package not found: $VsixPath"
}

if ([string]::IsNullOrWhiteSpace($env:VSCE_PAT)) {
    Write-Warning "VSCE_PAT is not configured. Skipping VS Code Marketplace publish."
    exit 0
}

$arguments = @(
    "@vscode/vsce",
    "publish",
    "--packagePath",
    $VsixPath,
    "--pat",
    $env:VSCE_PAT,
    "--skip-duplicate"
)

if ($IsPrerelease) {
    $arguments += "--pre-release"
}

& npx @arguments

if ($LASTEXITCODE -ne 0) {
    throw "VS Code Marketplace publish failed with exit code $LASTEXITCODE."
}
