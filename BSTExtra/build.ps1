#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DalamudLib = Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev\"
$OutDir = Join-Path $env:APPDATA "XIVLauncher\devPlugins\BSTExtra.104\"

if (-not (Test-Path (Join-Path $DalamudLib "Dalamud.dll"))) {
    Write-Host "ERROR: Dalamud が見つかりません: $DalamudLib" -ForegroundColor Red
    exit 1
}

Write-Host "=== BST Extra 1.0.4 build ===" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 古い Rotation.dll が残っていると誤って使われるので消す
Remove-Item -Force -ErrorAction SilentlyContinue @(
    (Join-Path $OutDir "BSTExtra.Rotation.dll"),
    (Join-Path $OutDir "BSTExtra.Rotation.pdb"),
    (Join-Path $OutDir "BSTExtra.Rotation.deps.json")
)

dotnet build (Join-Path $Root "BSTExtra.csproj") -c Release /p:DalamudLibPath="$DalamudLib" /p:OutputPath="$OutDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Output: $OutDir" -ForegroundColor Green
Write-Host "次にゲームを再起動し、/xlplugins で BST Extra Rotation をオンにしてください。" -ForegroundColor Green
Write-Host "チャットに [BST Extra] v1.0.4 と出ることを確認してください。" -ForegroundColor Green
