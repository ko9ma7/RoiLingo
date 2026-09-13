@echo off
chcp 65001 >nul
setlocal

echo [CHECK] LibreTranslate local server
where docker >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Docker를 찾을 수 없습니다.
  echo [INFO] Docker Desktop 설치 후 다시 실행하세요: https://www.docker.com/products/docker-desktop/
  pause
  exit /b 1
)

docker inspect roilingo-libretranslate >nul 2>nul
if not errorlevel 1 (
  echo [CHECK] 기존 RoiLingo LibreTranslate 컨테이너 시작
  docker start roilingo-libretranslate >nul
  if errorlevel 1 goto :failed
  echo [OK] http://localhost:5000
  pause
  exit /b 0
)

echo [CHECK] libretranslate/libretranslate:latest 컨테이너 생성
rem 모델은 최초 실행 때 내려받기 때문에 첫 시작은 시간이 걸릴 수 있습니다.
docker run -d --name roilingo-libretranslate --restart unless-stopped -p 127.0.0.1:5000:5000 -v roilingo-libretranslate-models:/home/libretranslate/.local libretranslate/libretranslate:latest
if errorlevel 1 goto :failed

echo [OK] LibreTranslate 시작 요청 완료: http://localhost:5000
echo [INFO] 최초 모델 준비 중에는 API가 바로 응답하지 않을 수 있습니다.
echo [INFO] 상태 확인: docker logs -f roilingo-libretranslate
pause
exit /b 0

:failed
echo [ERROR] LibreTranslate 컨테이너 시작에 실패했습니다.
echo [RECOVERY] docker logs roilingo-libretranslate
pause
exit /b 1
