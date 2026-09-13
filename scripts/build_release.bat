@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0\.."
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET 8 SDK가 설치되어 있지 않습니다.
  echo [RECOVERY] Visual Studio 2022의 '.NET 데스크톱 개발' 워크로드와 .NET 8 SDK를 설치하세요.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_release.ps1"
set EXITCODE=%ERRORLEVEL%
if not "%EXITCODE%"=="0" (
  echo [ERROR] Release 빌드 실패
  pause
  exit /b %EXITCODE%
)

echo [OK] Release 빌드 완료
pause
exit /b 0
