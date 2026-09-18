# RoiLingo 2.2.0

## 목표

기존의 고정 ROI 실시간 감시는 유지하면서, 게임 외 화면에서도 바로 쓸 수 있는 **one-shot screen translator** 기능을 추가했습니다. 기본 UI는 계속 작게 유지합니다.

## 추가 기능

- `빠른번역` 버튼: 현재 화면을 freeze-frame으로 캡처한 뒤 드래그한 영역만 OCR/번역
- 전역 단축키
  - `Ctrl+Alt+T`: 영역 선택 번역
  - `Ctrl+Alt+W`: 현재 활성 창 전체 번역
  - `Ctrl+Alt+V`: 클립보드 텍스트 번역
- 빠른 결과 창: 드래그/리사이즈, 원문/번역 표시, 복사
- 빠른 번역 기록도 기존 JSONL history에 자동 저장
- 빠른 번역은 모든 설정 Provider를 시작하고 **첫 번째 검증 성공 결과**를 반환하는 저지연 경로 사용
- 고정 ROI 감시는 기존 교차검증 정책 유지
- `scripts/diagnose.cmd`: .NET/WebView2/OCR 모델/최근 로그/필수 소스 파일을 한 번에 점검

## 기존 안정성 유지

- 가려진/비활성 대상 창: BackgroundFirst 캡처 유지
- 창 이동: normalized ROI 좌표 유지
- 기존 ROI 이동/리사이즈/삭제 편집 유지
- 순간 메시지 snapshot + bounded queue 유지
- 번역 완료 전 화면이 사라져도 OCR/번역/기록 완료
- Overlay width/height 독립 조절 유지
- Settings를 열지 않아도 백그라운드 WebView 번역 엔진 사용
- API/LibreTranslate와 무료 Web 교차검증 모두 유지

## 캐시

이 버전은 `translation-cache-hybrid-v10.json`을 사용합니다.
