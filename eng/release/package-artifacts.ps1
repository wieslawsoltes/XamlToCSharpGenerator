[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$OutputRoot = './artifacts'
)

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$nugetDir = Join-Path $OutputRoot 'nuget'
$vsixPath = Join-Path $OutputRoot ("vsix/axsg-language-server-{0}.vsix" -f $Version)

& (Join-Path $scriptDir 'pack-nuget-artifacts.ps1') -Version $Version -OutputDir $nugetDir
& (Join-Path $scriptDir 'package-vscode-extension.ps1') -Version $Version -OutputVsixPath $vsixPath

Write-Host "Packaged NuGet artifacts to $nugetDir"
Write-Host "Packaged VS Code extension to $vsixPath"
