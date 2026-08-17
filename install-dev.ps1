#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Configuration = if ($args[0]) { $args[0] } else { "Release" }
$SourceDll = Join-Path $Root "out\$Configuration\Umbra.AutoRetainer.dll"
$DestDir = Join-Path $env:APPDATA "XIVLauncher\devPlugins\Umbra.AutoRetainer"

if (-not (Test-Path $SourceDll)) {
    Write-Host "Running build.ps1 first..." -ForegroundColor Yellow
    & (Join-Path $Root "build.ps1") $Configuration
}

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
Copy-Item -Force $SourceDll (Join-Path $DestDir "Umbra.AutoRetainer.dll")

$Pdb = [System.IO.Path]::ChangeExtension($SourceDll, ".pdb")
if (Test-Path $Pdb) {
    Copy-Item -Force $Pdb (Join-Path $DestDir "Umbra.AutoRetainer.pdb")
}

Write-Host ""
Write-Host "Copied: $DestDir\Umbra.AutoRetainer.dll" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Open Umbra settings in-game"
Write-Host "2. Enable Custom Plugins"
Write-Host "3. Add this DLL in Plugins:"
Write-Host "   $DestDir\Umbra.AutoRetainer.dll"
Write-Host "4. Add the AutoRetainer widget to the toolbar"
