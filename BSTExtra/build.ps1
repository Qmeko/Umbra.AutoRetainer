#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DalamudLib = Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev\"
$OutDir = Join-Path $env:APPDATA "XIVLauncher\devPlugins\BSTExtra\"
$RsrRoot = Join-Path $env:APPDATA "XIVLauncher\installedPlugins\RotationSolver"

if (-not (Test-Path (Join-Path $DalamudLib "Dalamud.dll"))) {
    Write-Host "ERROR: Dalamud が見つかりません: $DalamudLib" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $RsrRoot)) {
    Write-Host "ERROR: Rotation Solver が見つかりません: $RsrRoot" -ForegroundColor Red
    exit 1
}

$RsrDir = Get-ChildItem $RsrRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
$RsrLib = $RsrDir.FullName + "\"
if (-not (Test-Path (Join-Path $RsrLib "RotationSolver.Basic.dll"))) {
    Write-Host "ERROR: RotationSolver.Basic.dll が見つかりません: $RsrLib" -ForegroundColor Red
    exit 1
}

Write-Host "=== BST Extra build ===" -ForegroundColor Cyan
Write-Host "RSR: $RsrLib"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

dotnet build (Join-Path $Root "BSTExtra.csproj") -c Release /p:DalamudLibPath="$DalamudLib" /p:OutputPath="$OutDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $Root "BSTExtra.Rotation.csproj") -c Release /p:DalamudLibPath="$DalamudLib" /p:RsrLibPath="$RsrLib" /p:OutputPath="$OutDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Output: $OutDir" -ForegroundColor Green
Write-Host "次にゲームを再起動し、/xlplugins で BST Extra Rotation をオンにしてください。" -ForegroundColor Green
