[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Changelog = "",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "scripts\Build-PortablePackage.ps1") `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -Changelog $Changelog `
    -ExpectedVersion $ExpectedVersion

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
