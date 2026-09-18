$ErrorActionPreference = "Continue"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appData = Join-Path $env:LOCALAPPDATA "RobloxLiveTranslator"
Write-Host "RoiLingo diagnostic report" -ForegroundColor Cyan
Write-Host "Time: $(Get-Date -Format o)"
Write-Host "OS: $([Environment]::OSVersion.VersionString)"
Write-Host "PowerShell: $($PSVersionTable.PSVersion)"
Write-Host "Project: $root"
Write-Host ""

Write-Host "[.NET]"
if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks } else { Write-Host "dotnet: NOT FOUND" -ForegroundColor Red }

Write-Host "`n[WebView2 Runtime]"
$edge = Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients' -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue } |
    Where-Object { $_.name -match 'WebView2' -or $_.name -match 'Edge WebView' }
if ($edge) { $edge | Select-Object name,pv | Format-Table -AutoSize } else { Write-Host "WebView2 registry entry not found (Evergreen runtime may still be installed per-user)." }

Write-Host "`n[App data] $appData"
if (Test-Path $appData) {
    Get-ChildItem $appData -Force | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize
    $tess = Join-Path $appData 'tessdata'
    if (Test-Path $tess) {
        Write-Host "OCR models:"
        Get-ChildItem $tess -Filter *.traineddata | Select-Object Name,Length | Format-Table -AutoSize
    }
    $latest = Get-ChildItem (Join-Path $appData 'logs') -Filter 'runtime-*.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Write-Host "Latest runtime log: $($latest.FullName)"
        Get-Content $latest.FullName -Tail 40
    }
} else { Write-Host "No RoiLingo app-data directory yet." }

Write-Host "`n[Project files]"
$required = @(
 'src\RobloxLiveTranslator\Quick\QuickCaptureWindow.xaml',
 'src\RobloxLiveTranslator\Quick\QuickResultWindow.xaml',
 'src\RobloxLiveTranslator\Services\GlobalHotkeyManager.cs',
 'src\RobloxLiveTranslator\Monitoring\MonitorEngine.cs'
)
foreach ($rel in $required) {
    $ok = Test-Path (Join-Path $root $rel)
    Write-Host ("{0} {1}" -f ($(if($ok){'[OK]'}else{'[MISSING]'}), $rel))
}

Write-Host "`nRun scripts\verify.cmd for restore + Release/x64 compile verification."
