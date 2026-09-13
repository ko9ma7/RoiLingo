@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0\.."
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK를 찾을 수 없습니다.
  echo [RECOVERY] https://dotnet.microsoft.com/download/dotnet/8.0 에서 .NET 8 SDK를 설치하세요.
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify.ps1"
set EXITCODE=%ERRORLEVEL%
if not "%EXITCODE%"=="0" (
  echo [ERROR] 검증 실패. 위의 첫 번째 CS 오류부터 확인하세요.
  exit /b %EXITCODE%
)

echo [OK] 검증 성공
exit /b 0
