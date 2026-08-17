#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "Umbra.AutoRetainer\Umbra.AutoRetainer.csproj"
$Configuration = if ($args[0]) { $args[0] } else { "Release" }

$DalamudLib = Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev\"
if (-not (Test-Path (Join-Path $DalamudLib "Dalamud.dll"))) {
    Write-Host "ERROR: Dalamud not found: $DalamudLib" -ForegroundColor Red
    exit 1
}

$UmbraRoot = Join-Path $env:APPDATA "XIVLauncher\installedPlugins\Umbra"
if (-not (Test-Path $UmbraRoot)) {
    Write-Host "ERROR: Umbra not found: $UmbraRoot" -ForegroundColor Red
    exit 1
}

Write-Host "=== Umbra.AutoRetainer build ($Configuration) ===" -ForegroundColor Cyan
dotnet build $Project -c $Configuration /p:DalamudLibPath="$DalamudLib"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

$OutDll = Join-Path $Root "out\$Configuration\Umbra.AutoRetainer.dll"
Write-Host ""
Write-Host "Output: $OutDll" -ForegroundColor Green
