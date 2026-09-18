# RoiLingo 1.8.0 변경 내역

## 이번 버전의 목표

1. `영어 → 한국어`로 설정했는데 `중국어(간체)`/`인도네시아어` 같은 웹 UI가 결과로 선택되던 문제 제거
2. RoiLingo 자체 창/로그/README/SHA-256가 OCR에 다시 들어오는 self-capture 루프 완화
3. 무료 Web 모드에서 Papago/Google/DeepL 교차검증을 실제로 수행
4. 원문 언어 / OCR 언어 / 번역 대상 언어를 명확히 분리
5. API/로컬 방식과 무료 Web 방식을 한 프로그램에서 명확하게 선택

## 근본 원인과 수정

### 1. 잘못된 캐시 검증 대상 언어

기존 캐시 조회 경로 일부가 저장된 번역 결과를 현재 목표 언어가 아닌 기본 검증 경로로 검사할 수 있었습니다. 이 때문에 웹페이지의 언어 선택 UI가 캐시에 남은 뒤 재사용될 여지가 있었습니다.

수정:

- 캐시 키를 `source language + target language + source text`로 구성
- 캐시 조회 시 반드시 현재 `targetLanguage`로 재검증
- 캐시 세대를 `translation-cache-hybrid-v6.json`으로 변경해 이전 오염 캐시 자동 격리

### 2. 웹페이지 전체를 너무 넓게 읽던 추출기

공개 번역 사이트는 실제 번역문 외에도 언어 메뉴, 배너, 로그인, 사이드패널, 번역 기록 등의 텍스트가 많습니다. 넓은 body/visible-DOM heuristic은 실제 번역문이 아닌 UI를 선택할 수 있었습니다.

수정:

- 사이트별 명시 selector
- 실제 원문 위치를 찾은 뒤 대응되는 번역 영역만 탐색
- copy bridge / copy button / WebView 화면 OCR fallback
- 라이브 경로에서 광범위 accessibility-tree 결과를 최종 후보로 사용하지 않음
- `중국어(간체)`, `인도네시아어`, Papago 배너, Google side panel 등 알려진 UI 문구 폐기
- 선택된 목표 언어의 문자 체계 비율 검사 추가

### 3. 목표 언어 상태 누수

사용자가 WebView에서 번역 언어를 수동으로 변경한 상태가 다음 요청에 영향을 줄 수 있었습니다.

수정:

- 모든 Web 요청에 source/target 언어를 다시 포함
- 요청 후 실제 원문 입력이 보이는지 확인
- deep-link가 무시되면 실제 입력 editor를 찾아 Chromium input 이벤트로 재입력
- compact UI의 source/target/mode 컨트롤은 실행 중 잠금

### 4. 무료 Web 교차검증이 일부 Provider만 실행되던 문제

API가 없을 때도 균형 전략이 Web Provider 하나만 선택하는 경로가 있었습니다.

수정:

- 기본 전략을 `WebOnly`로 변경
- WebOnly 또는 API/local Provider가 전혀 없는 경우 활성화된 Papago/Google/DeepL Web을 모두 스케줄
- Web 교차검증에서는 캐시가 실제 웹 요청을 short-circuit하지 않음

### 5. 원문/OCR/번역 언어 계약 분리

추가:

- compact UI `원문` 선택
- compact UI `→ 번역` 선택
- `OCR 언어를 원문 언어에 맞춤` 옵션
- 중앙 `TranslationLanguages` 매핑
- Tesseract/Papago/Google/DeepL/LibreTranslate별 언어 코드 변환
- DeepL source language는 `EN`과 같이 source용 코드를 사용하고 target은 `EN-US` 등 target용 코드를 별도 사용

### 6. Self-capture

화면 캡처 fallback이 desktop screen-copy로 동작할 때 RoiLingo 자체가 ROI 위를 가리면 OCR에 프로그램 UI가 섞일 수 있었습니다.

수정:

- MainWindow, OverlayWindow, LiveTranslationWindow, OverlaySettingsWindow에 `WDA_EXCLUDEFROMCAPTURE` 적용 시도
- 지원되지 않는 환경에서는 `WDA_MONITOR` fallback
- OCR 텍스트에서 RoiLingo/WEBREAD/README/SHA-256/로그/Provider 상태 등 self-capture marker를 추가 검사
- self-capture로 판단되면 이전 ROI 번역도 정리하고 새 번역 요청을 만들지 않음

## 번역 모드

- `무료 · Web 3종 교차검증`: enabled Web Provider 전부
- `API/로컬 우선 · Web 보조검증`: API/local 1 + Web 1
- `API/로컬만 (안정 우선)`: API/local 최대 2개 회전
- `전체 Provider 교차검증`: 설정된 Provider 전부

## 검증

이 배포본에서 수행하는 정적 검증:

- 모든 XAML/csproj/manifest XML 파싱
- XAML 이벤트 핸들러와 code-behind 연결 검사
- 중복 `x:Name` 검사
- GitHub Actions YAML 파싱
- WebView에 주입되는 JavaScript를 추출해 Node 구문 검사
- C# delimiter/string 구조 검사
- ZIP 무결성 검사

현재 제작 환경에는 Windows WPF용 .NET SDK가 없어 실제 Windows 실행 파일 컴파일을 여기서 수행할 수 없습니다. 최종 컴파일 검증은 Windows에서 `scripts\verify.cmd`가 수행합니다.
