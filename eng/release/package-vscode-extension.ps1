[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$OutputVsixPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$extensionDir = Join-Path $repoRoot 'tools/vscode/axsg-language-server'
$packageJsonPath = Join-Path $extensionDir 'package.json'

if (($Version.Contains('/') -or $Version.Contains('\') -or $Version.EndsWith('.vsix')) -and $OutputVsixPath -and -not ($OutputVsixPath.Contains('/') -or $OutputVsixPath.Contains('\') -or $OutputVsixPath.EndsWith('.vsix'))) {
    $legacyOutputVsixPath = $Version
    $Version = $OutputVsixPath
    $OutputVsixPath = $legacyOutputVsixPath
}

function Resolve-AbsolutePath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

if ([string]::IsNullOrWhiteSpace($OutputVsixPath)) {
    $OutputVsixPath = "./artifacts/vsix/axsg-language-server-$Version.vsix"
}

$resolvedOutputVsixPath = Resolve-AbsolutePath $OutputVsixPath

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw ("Command failed with exit code {0}: {1} {2}" -f $LASTEXITCODE, $FilePath, ($Arguments -join ' '))
    }
}

function Invoke-ExternalCommandCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $output = & $FilePath @Arguments 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw ("Command failed with exit code {0}: {1} {2}{3}{4}" -f $LASTEXITCODE, $FilePath, ($Arguments -join ' '), [Environment]::NewLine, ($output -join [Environment]::NewLine))
    }

    return ($output -join [Environment]::NewLine).Trim()
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputVsixPath) | Out-Null

$backupPath = Join-Path ([System.IO.Path]::GetTempPath()) ("axsg-package-{0}.json" -f [guid]::NewGuid().ToString('N'))
Copy-Item $packageJsonPath $backupPath -Force
$vscodeVersion = Invoke-ExternalCommandCapture node (Join-Path $repoRoot 'eng/release/resolve-vscode-extension-version.mjs') $Version

function Restore-PackageJson {
    if (Test-Path $backupPath) {
        Copy-Item $backupPath $packageJsonPath -Force
        Remove-Item $backupPath -Force
    }
}

try {
    $packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
    $packageJson.version = $vscodeVersion
    $packageJson | ConvertTo-Json -Depth 100 | Set-Content $packageJsonPath -Encoding UTF8

    Push-Location $extensionDir
    try {
        Write-Host ("Packaging VS Code extension release {0} as Marketplace version {1}" -f $Version, $vscodeVersion)
        Invoke-ExternalCommand npm ci

        $vsceArguments = @(
            '@vscode/vsce',
            'package',
            '--out',
            $resolvedOutputVsixPath
        )

        Invoke-ExternalCommand npx @vsceArguments
    }
    finally {
        Pop-Location
    }
}
finally {
    Restore-PackageJson
}
