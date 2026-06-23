# 极客 OCR v4.0 一键打包脚本（本地）
# 产出框架依赖的 x64 发布包（目标机需 .NET 10 Desktop Runtime + Windows App SDK 1.7 Runtime）
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $scriptDir

# 自动定位 VS MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = (& $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1)
if (-not $msbuild) {
    Write-Host "[ERROR] Visual Studio MSBuild not found. Install VS 2022+ with '.NET desktop development'." -ForegroundColor Red
    exit 1
}

Write-Host "[1/4] Cleaning old build..." -ForegroundColor Cyan
Remove-Item -Recurse -Force bin, obj, dist -ErrorAction SilentlyContinue

Write-Host "[2/4] Building (Release x64, framework-dependent)..." -ForegroundColor Cyan
& $msbuild OcrTranslator.csproj -p:Configuration=Release -p:Platform=x64 -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -restore
if ($LASTEXITCODE -ne 0) { Write-Host "[FAILED] Build error." -ForegroundColor Red; exit 1 }

$publishDir = "bin\x64\Release\net10.0-windows10.0.19041.0"
if (-not (Test-Path $publishDir)) { Write-Host "[ERROR] Publish dir not found: $publishDir" -ForegroundColor Red; exit 1 }

Write-Host "[3/4] Zipping..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path dist -Force | Out-Null
$zip = Join-Path $scriptDir "dist\OcrTranslator_v4_x64.zip"
Compress-Archive -Path "$publishDir\*" -DestinationPath $zip -Force

Write-Host "[4/4] Done!" -ForegroundColor Green
Write-Host "Package : $zip"
Write-Host "Size    : $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Framework-dependent. Target machine needs:" -ForegroundColor Yellow
Write-Host "  - .NET 10.0 Desktop Runtime"
Write-Host "  - Windows App SDK 1.7 Runtime"
Write-Host "  Download: https://aka.ms/windowsappsdk/1.7/latest"
