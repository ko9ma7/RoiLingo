$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

Write-Host "[CHECK] .NET SDK"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK가 없습니다. winget install --id Microsoft.DotNet.SDK.8 -e"
}
$version = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
    throw ".NET SDK 버전을 확인하지 못했습니다. 'dotnet --list-sdks'를 실행해 설치 상태를 확인하세요."
}
$stableVersionText = ($version -split '-', 2)[0]
$sdkVersion = [Version]$stableVersionText
if ($sdkVersion.Major -lt 8) {
    throw "이 프로젝트는 .NET SDK 8 이상이 필요합니다. 현재: $version"
}
Write-Host "[OK] .NET SDK $version"

Write-Host "[CHECK] XAML well-formedness"
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
Get-ChildItem -Path .\src\RobloxLiveTranslator -Filter *.xaml -Recurse | ForEach-Object {
    try {
        # Windows PowerShell 5.1의 Get-Content 기본 인코딩(ANSI)을 사용하면 UTF-8 한글 XAML의
        # 멀티바이트가 따옴표까지 삼켜 잘못된 XML로 보일 수 있다. 바이트를 UTF-8로 직접 읽는다.
        $text = [System.IO.File]::ReadAllText($_.FullName, $utf8)
        $xml = New-Object System.Xml.XmlDocument
        $xml.PreserveWhitespace = $true
        $xml.LoadXml($text)
    }
    catch {
        throw "XAML parse failed: $($_.FullName)`n$($_.Exception.Message)"
    }
}
Write-Host "[OK] XAML parse"

Write-Host "[CHECK] RoiLingo 2.0 runtime contracts"
$contractFiles = @{
    "src\RobloxLiveTranslator\Models\AppSettings.cs" = @('SourceLanguage { get; set; } = "auto"', 'OcrLanguages { get; set; } = "eng+kor"', 'SmartMixedText { get; set; } = true', 'TargetLanguage { get; set; } = "ko"', 'TranslationStrategy { get; set; } = "WebOnly"')
    "src\RobloxLiveTranslator\MainWindow.xaml.cs" = @('translation-cache-hybrid-v8.json', 'WindowCaptureService(_settings.CaptureMode)', 'WarmUpAfterStartAsync', 'ApplyUiLanguage')
    "src\RobloxLiveTranslator\Translation\MultiTranslator.cs" = @('_strategy.Equals("WebOnly"', 'return web;')
    "src\RobloxLiveTranslator\Translation\TranslationTextValidator.cs" = @('MatchesTargetScript', '중국어(간체)', '인도네시아어')
    "src\RobloxLiveTranslator\Translation\MixedLanguageTextProcessor.cs" = @('already-target-language', 'mixed-filtered', 'ExtractForeignRuns')
    "src\RobloxLiveTranslator\OcrLanguagePickerWindow.xaml.cs" = @('SelectedLanguages', 'eng', 'kor')
    "src\RobloxLiveTranslator\Services\WindowCaptureService.cs" = @('PrintWindow-client', 'screen-foreground-fallback', 'BackgroundOnly')
    "src\RobloxLiveTranslator\RoiEditorWindow.xaml.cs" = @('Resize_DragDelta', 'Roi_MouseMove', 'DeleteSelected')
    "src\RobloxLiveTranslator\Overlay\OverlayWindow.xaml.cs" = @('OverlayHeightScale', '가로/세로 독립')
    "src\RobloxLiveTranslator\Services\UiText.cs" = @('ko-KR', 'en-US', 'ja-JP', 'zh-CN')
}
foreach ($relativePath in $contractFiles.Keys) {
    $fullPath = Join-Path $root $relativePath
    if (-not (Test-Path $fullPath)) { throw "Required source file missing: $relativePath" }
    $source = [System.IO.File]::ReadAllText($fullPath, $utf8)
    foreach ($requiredText in $contractFiles[$relativePath]) {
        if (-not $source.Contains($requiredText)) {
            throw "RoiLingo contract check failed: '$requiredText' not found in $relativePath"
        }
    }
}
Write-Host "[OK] runtime contracts"

Write-Host "[CHECK] NuGet restore"
dotnet restore .\RobloxLiveTranslator.sln
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)" }
Write-Host "[OK] restore"

Write-Host "[CHECK] Release/x64 build"
dotnet build .\RobloxLiveTranslator.sln -c Release -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
Write-Host "[OK] build"

Write-Host "[OK] Verification completed."
