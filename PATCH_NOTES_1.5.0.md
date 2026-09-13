# RoiLingo v1.5.0

이번 버전은 “OCR은 계속 되는데 번역이 늦거나 안 보임”, “여러 ROI가 순차적으로 밀림”, “이전 문장이 화면에 계속 남음”, “웹페이지에는 번역이 보이는데 프로그램이 결과를 못 가져옴” 문제를 구조적으로 수정한 버전입니다.

## P0 — 실시간 처리 파이프라인 변경

기존 `MonitorEngine`은 ROI 하나에서 OCR 후 웹 번역이 끝날 때까지 `await`한 뒤 다음 ROI로 넘어갔습니다. 웹 번역이 5~10초 걸리면 뒤 ROI의 OCR/번역도 같이 늦어질 수 있었습니다.

1.5.0부터는 번역을 캡처/OCR 루프에서 기다리지 않습니다.

- ROI마다 `revision`을 유지합니다.
- 새 OCR 문장이 확인되면 해당 ROI의 이전 번역 작업을 취소합니다.
- 늦게 끝난 이전 번역 결과는 revision 검사에서 폐기합니다.
- WebView provider의 대기 큐에서도 취소 토큰이 전달되어 오래된 요청이 계속 실행되지 않게 합니다.
- 번역 실패 시 같은 문장이 화면에 계속 있으면 `ForceOcrSeconds` 주기로 자동 재시도할 수 있게 상태를 되돌립니다.

결과적으로 **최신 문장만 화면에 도착하는 latest-wins 방식**입니다.

## P0 — 이전 번역이 남는 문제

- 새 OCR 문장이 확정되는 순간 해당 ROI의 이전 게임 오버레이를 즉시 지웁니다.
- ROI가 실제로 빈 화면/비텍스트 상태로 바뀌면 이전 번역을 제거합니다.
- force OCR 한 번이 일시적으로 실패했다고 정적인 기존 번역을 지우지는 않습니다. 실제 화면 변경 후 안정화된 상태에서 비텍스트가 확인된 경우에만 제거합니다.

별도 `실시간 번역 창`은 기록형 창이므로 기존처럼 과거 항목을 볼 수 있습니다. 게임 위 클릭 통과 오버레이는 ROI별 최신 결과만 표시합니다.

## P0 — 웹에는 번역이 보이는데 못 읽는 문제

웹 번역 결과 추출 순서를 강화했습니다.

1. 사이트별 알려진 selector
2. **text-node geometry** — 부모 컨테이너 전체가 아니라 실제 화면에 그려진 텍스트 노드의 위치를 기준으로 오른쪽 번역 문장을 찾음
3. source/target layout anchor
4. visible DOM snapshot
5. DOM heuristic
6. body text
7. Chromium accessibility tree
8. **copy bridge** — 사이트의 복사 버튼이 `navigator.clipboard.writeText`/`document.execCommand('copy')`에 전달하는 실제 문자열을 WebView2 message로 직접 수집
9. Windows clipboard fallback
10. **WebView visual OCR fallback** — 마지막 수단으로 WebView 화면의 오른쪽 번역 패널을 캡처하고 Tesseract로 보이는 번역문 자체를 OCR

즉, 사람이 WebView에서 번역문을 볼 수 있는데 DOM 구조 때문에 못 읽는 경우에도 마지막에는 “보이는 화면”을 읽도록 했습니다.

## 캐시 안전성

- 캐시 파일을 `translation-cache-hybrid-v3.json`으로 변경했습니다.
- `한국어 영어 일본어 중국어...` 같은 언어 선택 메뉴, 페이지 UI, 내부 실패 문구는 Provider 결과 단계와 캐시 조회 단계 모두에서 폐기합니다.
- 1.4 이하의 잘못된 웹 추출 결과는 새 캐시에 자동 승계되지 않습니다.

## OCR 모델

WebView 화면 OCR 폴백에 필요한 목표 언어 Tesseract 모델도 시작 시 함께 확인합니다. 예를 들어 영어 OCR → 한국어 번역이면 `eng`와 `kor` 모델을 준비합니다.

## 로그에서 확인할 성공 방식

정상적으로 웹 결과를 읽으면 다음 중 하나가 표시될 수 있습니다.

```text
WEBREAD Papago Web: 결과 읽기 성공 (text-node-geometry, ...ms)
WEBREAD Papago Web: 결과 읽기 성공 (copy-bridge, ...ms)
WEBREAD Papago Web: 결과 읽기 성공 (copy-button-clipboard, ...ms)
WEBREAD Papago Web: 결과 읽기 성공 (webview-visual-ocr, ...ms)
```

새 OCR이 이전 번역을 대체할 때는:

```text
PENDING ROI 1 ...
CLEAR   ROI 1 ...
```

같은 상태 로그도 확인할 수 있습니다.
