<#
.SYNOPSIS
  Builds a self-contained, single-file GrayScope 2 for Windows x64 into ./publish.
.EXAMPLE
  ./scripts/publish.ps1
  ./scripts/publish.ps1 -Version 0.2.0
#>
param(
    [string]$Version = "",
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\$Runtime"

dotnet test (Join-Path $root "GrayScope2.slnx") -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not publishing." }

$publishArgs = @(
    "publish", (Join-Path $root "src\GrayScope.App\GrayScope.App.csproj"),
    "-c", "Release", "-r", $Runtime, "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $out
)
if ($Version) { $publishArgs += "-p:Version=$Version" }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Write-Host "Published to $out"
