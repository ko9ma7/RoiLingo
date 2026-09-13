# RoiLingo 사용 설명서

## 1. 목적

RoiLingo는 Roblox 같은 Windows 게임 화면에서 사용자가 지정한 ROI 영역의 글자만 감시하고, 내용이 바뀔 때 Tesseract OCR을 실행한 뒤 WebView2 번역 사이트, 공식 번역 API, 또는 로컬 LibreTranslate/Argos 서버에서 번역 결과를 받아 게임 위에 표시하는 프로그램입니다.

API 키는 기본 기능에 필요하지 않습니다.

## 2. 처음 실행

1. RoiLingo 실행
2. `번역 웹 / 교차 검증` 탭 확인
3. Papago / Google / DeepL 탭이 정상적으로 열리는지 확인
4. 쿠키 동의, 로그인, CAPTCHA가 표시되면 사용자가 웹 탭에서 직접 처리
5. `번역 결과 읽기 테스트`를 눌러 각 사이트에서 결과를 실제로 읽어오는지 확인
6. 기본적으로 세 번역 사이트가 모두 활성화되어 있으며 한국어 번역의 우선 결과는 `Papago Web`

## 3. Roblox 창 선택

1. Roblox 실행
2. RoiLingo에서 `1. 대상 창 선택`
3. RoiLingo가 잠시 숨겨지면 Roblox 창을 한 번 클릭
4. 상단에 선택된 창 제목과 HWND가 표시되는지 확인

## 4. ROI 만들기

1. `2. ROI 편집`
2. 번역하고 싶은 게임 글자 영역을 마우스로 드래그
3. 여러 영역을 각각 만들 수 있음
4. ROI 목록에서 이름, 사용 여부, 오버레이, 이벤트 알림을 설정

ROI는 대상 창 크기에 대한 비율 좌표로 저장되므로 창 크기가 바뀌어도 최대한 같은 위치를 따라갑니다.

## 5. OCR 언어

영어 Roblox는 기본값:

```text
eng
```

영어 + 한국어:

```text
eng+kor
```

영어 + 일본어:

```text
eng+jpn
```

언어팩을 많이 넣을수록 OCR 비용이 증가할 수 있으므로 실제로 필요한 언어만 사용하는 것이 좋습니다.

## 6. 실시간 번역

`3. 실시간 번역 시작`을 누르면:

```text
창 캡처
  -> ROI 잘라내기
  -> 저해상도 변화 검사
  -> 변화 없음: 종료
  -> 변화 있음: 짧게 안정화 대기
  -> Tesseract OCR
  -> 이전 글자와 거의 같음: 종료
  -> 번역 캐시 확인
  -> Papago / Google / DeepL 웹 번역 병렬 실행
  -> 결과 수집 / 교차 비교
  -> 우선 Provider 선택
  -> 게임 위 오버레이
  -> 자동 기록 저장
```

따라서 화면을 계속 캡처하더라도 Tesseract와 웹 번역을 매 프레임 실행하지 않습니다.

### 이동/크기 조절 가능한 실시간 번역 창

`설정 / ROI`에서 **이동/크기 조절 가능한 실시간 번역 창 사용**을 켜면 별도의 번역 창이 열립니다.

- 제목 표시줄을 끌어 위치 이동
- 창 가장자리/모서리로 크기 조절
- 항상 위 켜기/끄기
- 원문 표시 켜기/끄기
- 번역 글자 크기 조절
- 창 위치와 크기 자동 저장

기존의 게임 위 클릭 통과 오버레이도 그대로 사용할 수 있습니다.

### 게임 위 반투명 오버레이 조절

`설정 / ROI`의 **게임 오버레이 위치/크기/투명도**를 누르면 ROI별 오버레이를 따로 조절할 수 있습니다.

- 가로/세로 위치 보정
- 오버레이 너비
- 배경 불투명도
- 번역 글자 크기
- OCR 원문 표시/숨김
- Provider / OCR 정확도 / 교차 일치도 표시/숨김

실시간 감시 중에도 변경 내용이 바로 반영되고 자동 저장됩니다.

별도의 실시간 번역 창을 닫은 뒤 다시 보고 싶으면 **실시간 번역 창 열기/다시 열기**를 누르면 됩니다.

## 7. 번역 기록

`번역 기록` 탭에는 다음이 정리됩니다.

- 시간
- ROI 이름
- OCR 원문
- 선택된 번역문
- 선택된 번역 사이트
- 교차 결과 일치도

행을 클릭하면 아래에서 Papago / Google / DeepL의 개별 결과와 응답 시간을 확인할 수 있습니다.

검색 상자에서 ROI 이름, 원문, 번역문, Provider 이름으로 필터링할 수 있습니다.

`CSV 내보내기`로 현재 불러온 기록을 CSV로 저장할 수 있습니다.

## 8. 자동 저장 위치

```text
%LocalAppData%\RobloxLiveTranslator\
```

주요 파일:

```text
settings.json
translation-cache-hybrid-v3.json
history\translations-YYYY-MM-DD.jsonl
logs\runtime-YYYY-MM-DD.log
exports\translations-*.csv
webview2\
```

`로그 폴더 열기` 버튼으로 바로 열 수 있습니다.

## 9. 번역 사이트 하나가 실패할 때

웹사이트 자동화는 사이트 UI 변경에 영향을 받습니다.

1. `번역 웹 / 교차 검증` 탭에서 실패한 사이트 직접 확인
2. 번역 자체가 안 되면 네트워크/사이트/로그인/동의 화면 문제
3. 사이트에서는 번역되는데 RoiLingo 결과만 실패하면 `번역 결과 읽기 테스트` 실행
4. 실행 로그에서 `WEBREAD ... known-selector / dom-heuristic / accessibility-tree` 성공 여부 확인
5. 1.5.0은 고정 selector 실패 시 실제 화면에 그려진 텍스트 노드 위치 → 원문/번역 패널 위치 매칭 → DOM/접근성 트리 → 복사 버튼 WebView bridge → Windows 클립보드 → WebView 화면 OCR 순서로 자동 폴백
6. 다른 Provider가 성공하면 RoiLingo는 성공한 결과로 계속 동작

어댑터 파일:

```text
src\RobloxLiveTranslator\Translation\Web\WebTranslationScripts.cs
```

## 10. GitHub 저장소 자동 생성 / 업로드

프로젝트 루트의:

```text
github-bootstrap.cmd
```

을 실행합니다.

이 파일은 `scripts\github-publish.ps1`을 호출합니다. 스크립트는 필요한 경우 Git, .NET SDK, GitHub CLI를 확인/설치하고 GitHub 로그인 후 `RoiLingo` 저장소 생성, About/Topics 설정, build, commit, push, Actions 확인, v1.6.1 tag와 Release 생성을 진행합니다.

명령 창이 바로 닫히지 않도록 마지막에 `pause`가 있습니다.

직접 실행:

```bat
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File scripts\github-publish.ps1
```


## API / 로컬 번역

`번역 API / 로컬` 탭에서 Web과 API를 함께 또는 따로 사용할 수 있습니다.

- **혼합 절약형**: 기본 추천. API/로컬 Provider 1개와 Web Provider 1개를 순환 선택합니다.
- **Web만**: Papago/Google/DeepL 웹 탭만 사용합니다.
- **API/로컬만**: API/LibreTranslate 중 최대 2개를 순환합니다.
- **최대 교차검증**: 활성 Provider를 모두 호출합니다. 유료 API의 크레딧/쿼터를 빠르게 쓸 수 있습니다.

API 키는 `api-secrets.dpapi`에 Windows DPAPI CurrentUser 방식으로 암호화됩니다. `settings.json`과 GitHub 저장소에는 평문 키가 들어가지 않습니다.

각 API에 `일 요청 한도`, `월 문자 한도`를 설정할 수 있습니다. 0은 RoiLingo 내부 제한 없음입니다. 이 카운터는 서비스 사업자의 실제 청구 한도와 별개입니다.

### LibreTranslate 로컬

Docker Desktop이 설치되어 있으면 프로젝트 폴더에서 다음 파일을 실행할 수 있습니다.

```bat
scripts\libretranslate-local.cmd
```

RoiLingo의 LibreTranslate Endpoint는 기본 `http://localhost:5000`입니다. 로컬 인스턴스는 일반적으로 API Key가 필요하지 않습니다.

## 웹 번역 결과를 화면에는 보이는데 못 가져올 때

1. `번역 웹 / 교차 검증` 탭에서 실제 번역이 화면에 표시되는지 확인합니다.
2. `복사 버튼/클립보드 폴백`을 켭니다.
3. `번역 결과 읽기 테스트`를 실행합니다.
4. 실행 로그에서 `WEBREAD ... 결과 읽기 성공 (...)`을 확인합니다.

1.5.0은 selector 하나에 의존하지 않고 selector → 화면 텍스트 노드 위치 → 원문/번역 패널 위치 매칭 → visible DOM → 일반 DOM → body text → accessibility tree → WebView 복사 bridge → 복사 버튼/클립보드 → WebView 화면 OCR 순으로 폴백합니다. 언어 선택 메뉴처럼 보이는 문자열은 번역 결과와 캐시 단계에서 제외합니다. 이전 버전의 잘못된 캐시는 `translation-cache-hybrid-v3.json`에서 자동 격리되며, 필요하면 **번역 캐시 비우기**를 누르세요.


## 여러 ROI가 밀리지 않게 하는 최신 문장 우선 처리

1.6.0부터 ROI OCR 루프는 웹 번역 완료를 기다리지 않되, 실행 중인 WebView 번역을 새 OCR 때문에 중간 취소하지 않습니다. ROI마다 실행 중 1개 + 최신 대기 1개만 유지하고, 더 오래된 대기 문장은 최신 문장으로 덮어씁니다. 완료된 결과의 revision이 오래되었으면 화면/기록에 반영하지 않습니다. 이 방식은 OCR이 자주 흔들려도 Papago/Google/DeepL 입력창이 계속 초기화되는 문제를 막습니다.

또한 새 문장이 확정되면 게임 위 오버레이의 이전 번역을 먼저 지우고 새 번역이 도착했을 때만 다시 표시합니다. ROI가 실제로 빈 화면으로 바뀌면 해당 ROI의 기존 번역도 제거됩니다. 별도의 **실시간 번역 창**은 기록용이므로 과거 항목을 계속 볼 수 있습니다.

## 게임 위 작은 번역 박스를 직접 편집하기 (1.6.0+)

실시간 번역 실행 중 **설정 / ROI → 게임 오버레이 직접 편집**을 누르세요. 별도의 실시간 번역 창이 아니라 게임 위에 뜨는 작은 반투명 번역 박스 자체를 조절합니다.

- 박스를 마우스로 드래그: 위치 이동
- 우하단 파란 핸들 드래그: 너비/글자 크기 조절
- 마우스 휠: 너비 조절
- Ctrl + 마우스 휠: 배경 투명도 조절
- Shift + 마우스 휠: 글자 크기 조절
- 다시 **게임 오버레이 편집 완료** 클릭: 클릭 통과 모드 복귀

설정은 ROI별로 자동 저장됩니다. **게임 오버레이 세부 설정** 창도 정밀 수치 조절용으로 그대로 사용할 수 있습니다.

## 웹 번역 페이지에 OCR 글이 들어가지 않을 때 (1.6.0+)

RoiLingo는 먼저 Papago/Google/DeepL의 deep-link URL로 원문을 전달합니다. 사이트 SPA 변경 때문에 URL의 원문이 무시되면 실제 화면의 왼쪽 입력 편집기를 찾아 OCR 문장을 직접 넣고 `input`/`change` 이벤트를 발생시킵니다. 실행 로그의 `WEBREAD ... 원문 전달 확인` 또는 `원문 재전달 성공/실패` 메시지로 이 단계를 확인할 수 있습니다.

