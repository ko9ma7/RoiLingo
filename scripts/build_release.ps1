$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

& (Join-Path $PSScriptRoot "verify.ps1")

$outDir = Join-Path $root "publish\win-x64"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

Write-Host "[CHECK] self-contained win-x64 publish"
dotnet publish .\src\RobloxLiveTranslator\RobloxLiveTranslator.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:Platform=x64 -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$zipPath = Join-Path $root "publish\RoiLingo-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "[OK] EXE: $outDir\RoiLingo.exe"
Write-Host "[OK] ZIP: $zipPath"
