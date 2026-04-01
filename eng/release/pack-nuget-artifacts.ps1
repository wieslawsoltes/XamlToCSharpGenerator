[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$OutputDir = './artifacts/nuget'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (($Version.Contains('/') -or $Version.Contains('\')) -and $OutputDir -and -not ($OutputDir.Contains('/') -or $OutputDir.Contains('\'))) {
    $legacyOutputDir = $Version
    $Version = $OutputDir
    $OutputDir = $legacyOutputDir
}

function Resolve-AbsolutePath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

$resolvedOutputDir = Resolve-AbsolutePath $OutputDir

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

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

$projects = @(
    'src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj',
    'src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj',
    'src/XamlToCSharpGenerator.Build/XamlToCSharpGenerator.Build.csproj',
    'src/XamlToCSharpGenerator.Compiler/XamlToCSharpGenerator.Compiler.csproj',
    'src/XamlToCSharpGenerator.Core/XamlToCSharpGenerator.Core.csproj',
    'src/XamlToCSharpGenerator.Editor.Avalonia/XamlToCSharpGenerator.Editor.Avalonia.csproj',
    'src/XamlToCSharpGenerator.ExpressionSemantics/XamlToCSharpGenerator.ExpressionSemantics.csproj',
    'src/XamlToCSharpGenerator.Framework.Abstractions/XamlToCSharpGenerator.Framework.Abstractions.csproj',
    'src/XamlToCSharpGenerator.Generator/XamlToCSharpGenerator.Generator.csproj',
    'src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj',
    'src/XamlToCSharpGenerator.LanguageService/XamlToCSharpGenerator.LanguageService.csproj',
    'src/XamlToCSharpGenerator.McpServer/XamlToCSharpGenerator.McpServer.csproj',
    'src/XamlToCSharpGenerator.MiniLanguageParsing/XamlToCSharpGenerator.MiniLanguageParsing.csproj',
    'src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj',
    'src/XamlToCSharpGenerator.RemoteProtocol/XamlToCSharpGenerator.RemoteProtocol.csproj',
    'src/XamlToCSharpGenerator.Runtime/XamlToCSharpGenerator.Runtime.csproj',
    'src/XamlToCSharpGenerator.Runtime.Avalonia/XamlToCSharpGenerator.Runtime.Avalonia.csproj',
    'src/XamlToCSharpGenerator.Runtime.Core/XamlToCSharpGenerator.Runtime.Core.csproj'
)

Push-Location $repoRoot
try {
    foreach ($project in $projects) {
        $dotnetPackArguments = @(
            'pack',
            $project,
            '-c',
            'Release',
            '-o',
            $resolvedOutputDir,
            '-m:1',
            '/nodeReuse:false',
            '--disable-build-servers',
            '/p:ContinuousIntegrationBuild=true',
            "/p:Version=$Version"
        )

        Invoke-ExternalCommand -FilePath dotnet -Arguments $dotnetPackArguments
    }
}
finally {
    Pop-Location
}
