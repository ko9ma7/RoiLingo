# RoiLingo 2.0.0

RoiLingo is a compact Windows WPF utility for **target-window ROI OCR → translation → game overlay**.
It is designed to stay small during normal use while keeping OCR, Web/API/local translation, history and diagnostics under Settings.

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

## What changed in 2.0

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

The game overlay now stores **width scale and height scale separately**.

During overlay edit mode:

- drag panel → move
- drag blue corner handle horizontally → width only
- drag blue corner handle vertically → height only
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

Translation still uses a latest-wins queue per ROI, so stale messages do not build an unlimited backlog.

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
10. creates/updates tag and Release **v2.0.0** and uploads `RoiLingo-win-x64.zip`.

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
translation-cache-hybrid-v8.json
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
