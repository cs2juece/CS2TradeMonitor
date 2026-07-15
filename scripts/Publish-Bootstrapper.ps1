[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectFile,
    [Parameter(Mandatory = $true)][string]$Configuration,
    [Parameter(Mandatory = $true)][string]$Runtime,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
$ProjectFile = [System.IO.Path]::GetFullPath($ProjectFile)
$PublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)

if ($Runtime -ne "win-x64") {
    throw "The native bootstrapper currently supports only win-x64."
}

$versionParts = @($Version.TrimStart('v', 'V').Split('.') | ForEach-Object {
    $value = 0
    [void][int]::TryParse(($_ -split '-')[0], [ref]$value)
    $value
})
while ($versionParts.Count -lt 4) { $versionParts += 0 }
$versionNumbers = ($versionParts[0..3] -join ',')
$generatedVersionDir = Join-Path ([System.IO.Path]::GetTempPath()) "CS2TradeMonitor-native-version"
New-Item -ItemType Directory -Path $generatedVersionDir -Force | Out-Null
$versionHeader = Join-Path $generatedVersionDir "GeneratedVersion.h"
@"
#pragma once
#define APP_VERSION_NUM $versionNumbers
#define APP_VERSION_STR "$Version\0"
"@ | Set-Content -LiteralPath $versionHeader -Encoding ascii

. (Join-Path $PSScriptRoot "Resolve-MSBuild.ps1")
$msbuild = Resolve-MSBuild

New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
$buildOutput = Join-Path (Split-Path -Parent $ProjectFile) "bin\Publish-$Configuration"
New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null
& $msbuild $ProjectFile /m /t:Rebuild "/p:Configuration=$Configuration" /p:Platform=x64 `
    "/p:GeneratedVersionDir=$generatedVersionDir" "/p:OutDir=$buildOutput\" /nologo
if ($LASTEXITCODE -ne 0) {
    throw "Native bootstrapper build failed with exit code $LASTEXITCODE."
}

$builtBootstrapperExe = Join-Path $buildOutput "CS2TradeMonitor.Bootstrapper.exe"
$bootstrapperExe = Join-Path $PublishDirectory "CS2TradeMonitor.Bootstrapper.exe"
if (!(Test-Path -LiteralPath $builtBootstrapperExe -PathType Leaf)) {
    throw "Bootstrapper build failed: CS2TradeMonitor.Bootstrapper.exe was not produced."
}
Copy-Item -LiteralPath $builtBootstrapperExe -Destination $bootstrapperExe -Force
