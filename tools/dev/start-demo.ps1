<#
.SYNOPSIS
  Runs GrayScope 2 against a local mock Graylog with an isolated, seeded data folder.
  Never touches the real %LOCALAPPDATA%\GrayScope2 data or any real Graylog server.
#>
param([string]$DataDir = (Join-Path $env:TEMP "gs2-demo"))
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Resolve-Path (Join-Path $here "..\..")
$exe = Join-Path $root "src\GrayScope.App\bin\Debug\net10.0\GrayScope2.exe"
if (-not (Test-Path $exe)) { dotnet build (Join-Path $root "src\GrayScope.App") | Out-Null }

$env:GRAYSCOPE2_DATA_DIR = $DataDir
$db = Join-Path $DataDir "grayscope2.db"

# First run creates the schema.
if (-not (Test-Path $db)) {
    $p = Start-Process $exe -PassThru
    Start-Sleep -Seconds 6
    $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2
    if (-not $p.HasExited) { $p.Kill() }
}

# Token protected exactly like the app does (DPAPI, CurrentUser, app entropy).
Add-Type -AssemblyName System.Security
$entropy = [Text.Encoding]::UTF8.GetBytes("GrayScope2.TokenProtection.v1")
$bytes = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes("mock-token"), $entropy,
    [Security.Cryptography.DataProtectionScope]::CurrentUser)
python -I (Join-Path $here "seed_demo.py") $db ("dpapi:" + [Convert]::ToBase64String($bytes))

if (-not (Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*mock_graylog*" })) {
    Start-Process python -ArgumentList "-I", (Join-Path $here "mock_graylog.py"), "9123" -WindowStyle Hidden
}
Start-Process $exe
Write-Host "Demo running. Data: $DataDir  Mock: http://127.0.0.1:9123/api"
