@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo [CHECK] Starting RoiLingo GitHub publisher...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\github-publish.ps1"
set EXITCODE=%ERRORLEVEL%
echo.
if "%EXITCODE%"=="0" (
  echo [OK] GitHub publish completed.
) else (
  echo [ERROR] GitHub publish failed with exit code %EXITCODE%.
  echo [INFO] See the first ERROR line above for the recovery command.
)
pause
exit /b %EXITCODE%
