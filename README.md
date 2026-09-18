# RoiLingo 2.2.0

> **2.2.0:** 고정 ROI 실시간 감시 외에 전역 단축키 기반 **빠른 영역 번역 / 활성 창 번역 / 클립보드 번역**을 추가했습니다. 기본 화면은 여전히 작게 유지하며, `대상 → ROI → 시작`만으로 기존 실시간 감시를 사용할 수 있습니다.


RoiLingo is a compact Windows WPF utility for **target-window ROI OCR → translation → game overlay**.
It is designed to stay small during normal use while keeping OCR, Web/API/local translation, history and diagnostics under Settings.

## Two workflows

### 1) Fixed ROI live monitor

```text
대상 → ROI → 시작
```

게임/영상의 고정 영역을 계속 감시하고, 짧게 나타났다 사라지는 메시지도 스냅샷으로 보존해 OCR/번역/로그를 완료합니다.

### 2) Quick translation anywhere

RoiLingo가 실행 중이면 대상 창이나 ROI가 없어도 사용할 수 있습니다.

```text
Ctrl+Alt+T  화면을 멈춘 뒤 드래그한 영역 OCR + 번역
Ctrl+Alt+W  현재 활성 창 전체 OCR + 번역
Ctrl+Alt+V  클립보드 텍스트를 OCR 없이 즉시 번역
```

작은 기본 창의 **빠른번역** 버튼은 영역 선택 모드이고, 우클릭하면 세 가지 빠른 동작을 선택할 수 있습니다. 결과 창은 자유롭게 이동/크기 조절할 수 있고 번역 복사도 가능합니다. 빠른 번역은 지연을 줄이기 위해 설정된 Provider들을 동시에 시작한 뒤 **첫 번째로 검증에 통과한 결과**를 표시합니다. 고정 ROI 모드는 기존 교차검증 정책을 그대로 유지합니다.

이 때문에 게임뿐 아니라 PDF, 이미지, Canvas, 웹소설, 잠긴 웹페이지, 영상 자막, 원격 데스크톱 화면, 복사한 텍스트에도 사용할 수 있습니다.

## Quick start

```text
[대상/Target] → [ROI] → [시작/Start]
```

Default translation setup:

```text
Source: Auto detect
OCR candidates: English + Korean (eng+kor)
Target: Korean
Mode: Free Web cross-check (Papago + Google + DeepL)
```

The compact UI can be switched between **한국어 / English / 日本語 / 简体中文** from the top toolbar.

## What changed in 2.1

### Background / inactive window capture

RoiLingo now uses a **background-first capture policy**. It first asks the selected HWND to render its client area with `PrintWindow`, so the capture does not depend on where the window is located on the desktop and can normally continue when another window covers it.

If background capture fails, RoiLingo only uses screen-copy fallback while the selected target itself is the foreground window. This prevents a covered target from accidentally OCRing the app that is covering it.

The capture mode can be changed under **Settings → General / ROI → Capture mode**:

- `BackgroundFirst` — recommended default
- `BackgroundOnly` — never use screen-copy fallback
- `Auto` — background first, foreground screen fallback allowed

Important limitation: some GPU-only/protected/minimized applications do not provide off-screen content to `PrintWindow`. RoiLingo treats that as a capture failure instead of OCRing unrelated screen pixels. Windows 10 1903+ exposes `GraphicsCaptureItem` for HWND capture; a future backend can plug into the same capture service without changing ROI/OCR/translation code.

### ROI movement and editing

ROI coordinates are stored as **normalized client coordinates (0..1)**, not absolute desktop coordinates. Moving the target window therefore does not invalidate ROI positions.

The ROI editor now supports existing ROI editing:

- drag empty space → add a new ROI
- click and drag an existing ROI → move it
- drag the blue bottom-right handle → resize width and height independently
- Delete / Backspace or **선택 삭제** → delete only the selected ROI
- Enter / **저장** → save

You no longer need to delete an ROI and recreate it just to adjust its bounds.

### Overlay resizing

The game overlay now stores **real width and height independently** after direct editing. Auto sizing remains available when width/height are 0.

During overlay edit mode:

- drag the blue move bar → move
- drag the right blue edge → width only
- drag the bottom blue edge → height only
- drag the bottom-right handle → width + height
- mouse wheel → width
- Shift + wheel → height
- Ctrl + wheel → opacity
- Alt + wheel → font size

The detailed overlay settings window also exposes width and height independently.

### Faster startup and faster event reaction

`Start` no longer waits for all WebView2 tabs and every OCR model to initialize first.

Pipeline in 2.0:

```text
Start
  → monitor begins immediately
  → OCR models initialize lazily on first use
  → WebView2 initializes lazily on first Web request
  → optional background warm-up runs after monitoring already started
```

Default timing was tightened for live events:

- ROI polling: 150 ms
- visual settle time: 100 ms
- translation provider window: 2.5 s
- EventMode ROIs are checked first and use a one-pass fast OCR path

Translation now uses a **small bounded per-ROI FIFO**. A short message is captured as a bitmap snapshot before it disappears, then OCR/translation continues even after the screen has changed. The queue is capped (normal ROI 4, event ROI 12) so it cannot grow without bound.


### Transient messages are retained and logged

A visual change now starts a short **snapshot burst**. RoiLingo keeps the most text-like frame from that burst in memory. If a game/admin message appears briefly and disappears before the ROI settles, OCR still runs on that retained bitmap.

The recognized source text is written to the runtime log immediately (`CAPTURE ...`) before translation finishes. Translation work is kept in a bounded FIFO so captured unique messages are not discarded merely because the ROI has already changed again. Successful translations are appended to the normal JSONL translation history.

The game overlay no longer disappears simply because the source text vanished. Completed translations remain visible for the ROI's configured hold time (default 20 s; event ROI at least 30 s). Set the hold time to 0 to keep the latest translation until it is replaced.

## Translation modes

| Mode | Behavior |
| --- | --- |
| **Free · 3 Web translators** | Papago / Google / DeepL WebView cross-check, no API key |
| **API/local first · Web verify** | One API/local provider + one Web verifier |
| **API/local only** | More deterministic unattended mode |
| **All providers cross-check** | All configured providers, highest quota use |

Supported API/local providers:

- NAVER Cloud Papago Text Translation
- Google Cloud Translation Basic v2
- DeepL API
- LibreTranslate-compatible endpoint / Argos-based local setups

API secrets are stored with Windows DPAPI for the current user and are not written to `settings.json` or Git.

## OCR and multilingual text

Translation language and OCR language are different concepts.

- **Source** can be `Auto detect`.
- **OCR languages** are candidate Tesseract models such as `eng+kor+jpn`.
- **Target** is the desired translation language.

For a bilingual ROI such as:

```text
Any good ideas? 좋은 생각 있어?
```

`eng+kor` OCR is recommended. Smart mixed-language processing can avoid re-translating text that is already in the target language.

## Build / verify

Requirements:

- Windows 10/11 x64
- .NET SDK 8+
- Microsoft Edge WebView2 Runtime for Web translation mode

Verify:

```bat
scripts\verify.cmd
```

Create release package:

```bat
scripts\build_release.bat
```

## GitHub uploader

Run:

```bat
github-bootstrap.cmd
```

The publisher:

1. checks Git / .NET / GitHub CLI;
2. checks `gh auth status` and starts browser login when required;
3. runs the project verifier and Release/x64 build;
4. creates or reuses the `RoiLingo` repository;
5. preserves existing `origin/main` history instead of force-pushing;
6. pushes the current complete source snapshot;
7. updates repository description/topics;
8. waits for the Windows GitHub Actions build when visible;
9. builds the self-contained Windows x64 ZIP;
10. creates/updates tag and Release **v2.2.0** and uploads `RoiLingo-win-x64.zip`.

No tokens/API keys are embedded in the uploader.

## Runtime data

Stored under:

```text
%LocalAppData%\RobloxLiveTranslator\
```

Typical files:

```text
settings.json
api-secrets.dpapi
translation-cache-hybrid-v10.json
history\translations-YYYY-MM-DD.jsonl
logs\runtime-YYYY-MM-DD.log
exports\translations-*.csv
webview2\...
```

## Architecture

```text
Target HWND
  ↓
Background-first client capture
  ↓
Normalized ROI crop
  ↓
Cheap change signature
  ↓ only changed/stable ROI
Tesseract OCR
  ↓
Mixed-language filtering
  ↓
Web / API / local translation providers
  ↓
Target-script validation + cross-check
  ↓
Current ROI overlay + history/log
```

## Notes on capture support

`PrintWindow` is a Win32 request to the target application to render itself into a supplied device context. It is useful for covered/inactive windows, but an application can still return blank/black content, especially if it stops rendering while minimized or uses protected/GPU-only surfaces. RoiLingo deliberately avoids pretending that a screen-copy of the covering app is the selected target.

## License

See `LICENSE`.


## Open-source reference projects

RoiLingo 2.2의 기능 확장은 아래 공개 프로젝트들의 **사용 흐름과 설계 아이디어**를 참고했습니다. 해당 프로젝트의 소스 코드를 복사해 포함하지 않았습니다.

- Kushisusumita/screen-translator — 전역 단축키, freeze-frame 영역 선택, first-success 번역 흐름, tray 중심 UX
- OneMoreGres/ScreenTranslator — 캡처/OCR/번역 모듈 분리, recognizer/translator 리소스 관리, 단축키 영역 번역
- bigone2000/screen-select-translate — 브라우저의 이미지/영상/Canvas 영역 OCR, 드래그 가능한 결과 창, UI i18n
- zixload/ocr-translate-overlay — 로컬 OCR, 떠 있는 패널, 위치/크기 기억, 모델 자동 준비
- meangrinch/MangaTranslator — OCR/번역 backend 교체 가능 구조, 다국어/배치 처리 아이디어

자세한 비교와 채택/보류 항목은 `docs/REFERENCE_PROJECTS.md`를 참고하세요.
