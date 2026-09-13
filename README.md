# RoiLingo

RoiLingo is a Windows WPF live OCR translator for games and other applications. It watches user-defined ROI regions, runs OCR only when a region changes, translates newly detected text, and shows the result in a click-through overlay and/or a movable live translation window.

Version **1.6.1** supports two translation paths at the same time:

- **WebView2 web translators**: Papago, Google Translate, DeepL — no API key required.
- **API/local translators**: Papago Text Translation API, Google Cloud Translation Basic API, DeepL API, and LibreTranslate/Argos-compatible servers.

The default **Hybrid Balanced** strategy calls one API/local provider and one web provider per new OCR text, rotating providers instead of spending every paid API quota on every event. Repeated text is served from the local translation cache.

## Main features

- Click a target game/window and track it by HWND.
- Draw multiple normalized ROI rectangles over the target window.
- Low-CPU visual change detection before OCR.
- Open-source **Tesseract 5** OCR with selectable language packs.
- OCR modes: Fast / Balanced / Accurate multi-pass page segmentation.
- Embedded WebView2 tabs for Papago, Google Translate, DeepL.
- Web result extraction fallbacks:
  - known selectors,
  - source/target **layout-anchored** result extraction,
  - rendered visible DOM snapshot with UI-chrome rejection,
  - generic DOM scoring,
  - whole-page visible body text,
  - Chromium accessibility tree,
  - optional target-side Copy-button/clipboard fallback.
- Official API integrations:
  - Papago Text Translation API
  - Google Cloud Translation Basic v2
  - DeepL API Free/Pro
- Open-source local translation through **LibreTranslate**, powered by Argos Translate.
- Hybrid provider rotation and local daily/monthly budget limits.
- DeepL `/v2/usage` actual usage lookup during API test.
- DPAPI-encrypted API secrets tied to the current Windows user.
- Translation cache with invalid web-UI/failure result rejection.
- **Coalescing latest-per-ROI**: a running WebView translation is allowed to finish, stale results are discarded, and only the newest pending OCR sentence is retained. This avoids cancelling the browser while it is still navigating/inserting text.
- The game overlay clears the previous sentence when a new sentence is confirmed or when the ROI becomes empty.
- Click-through game overlay with per-ROI position offset, width, background opacity, font size, source/meta visibility.
- Separate movable/resizable always-on-top live translation window with saved position/size/font.
- Translation history, provider comparison, CSV export, daily runtime logs.
- Windows GitHub Actions build and GitHub Release packaging.
- One-click GitHub publishing entry point: `github-bootstrap.cmd`.

## Runtime architecture

```text
Target window
    |
    v
one capture frame
    |
    +--> ROI change signature -- unchanged --> skip
    |
    +--> changed + settled
            |
            v
       Tesseract OCR
            |
            v
       text dedupe
            |
   per-ROI coalescing worker
 (keep only newest pending text)
            |
            +---------------- Hybrid scheduler ----------------+
            |                                                  |
            v                                                  v
     API / local rotation                               WebView rotation
 Papago / Google / DeepL / Libre                  Papago / Google / DeepL
            |                                                  |
            +------------------- results ----------------------+
                                |
                     preferred result + agreement
                                |
                   overlay / live window / history
```


## v1.6 reliability changes

- Web translators now verify that the OCR source text actually appeared in the source editor. If a deep-link URL is ignored by the site, RoiLingo injects the text into the visible source editor and dispatches real `input`/`change` events before waiting for the result.
- Game overlay boxes can be edited **directly on top of the target window** while translation is running: drag to move, drag the lower-right handle to resize, `Ctrl+mouse wheel` to change opacity, and `Shift+mouse wheel` to change font size. Click **게임 오버레이 직접 편집** again to restore click-through mode.
- GitHub bootstrap now fetches an existing `origin/main` first. If the remote was created by an earlier RoiLingo bootstrap and has unrelated history, the current release snapshot is committed on top of the remote history instead of failing with `fetch first` or force-pushing.

## Requirements

- Windows 10/11 x64
- .NET SDK 8 or newer when building from source
- Microsoft Edge WebView2 Runtime when using web translation
- Internet connection for web and cloud API providers
- Optional Docker Desktop for local LibreTranslate

## Build

Open `RobloxLiveTranslator.sln` in Visual Studio 2022 with the **.NET desktop development** workload, or run:

```bat
scripts\verify.cmd
```

Manual:

```powershell
dotnet restore RobloxLiveTranslator.sln
dotnet build RobloxLiveTranslator.sln -c Release -p:Platform=x64
```

Release package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build_release.ps1
```

Output:

```text
publish\win-x64\RoiLingo.exe
publish\RoiLingo-win-x64.zip
```

## First use

1. Start RoiLingo.
2. In **번역 웹 / 교차 검증**, enable the web translators you want. If a site shows a cookie/login/consent screen, handle it directly in that tab.
3. In **번역 API / 로컬**, choose a strategy:
   - `혼합 절약형`: recommended; one API/local + one web provider per text, rotating providers.
   - `Web만`: browser translators only.
   - `API/로컬만`: up to two API/local providers rotate.
   - `최대 교차검증`: all enabled providers are called; this can consume paid quota quickly.
4. Optionally enter API credentials. Click **API 설정/비밀키 저장**.
5. Click **대상 창 선택**, then click Roblox or another target window.
6. Click **ROI 편집** and draw the text regions.
7. Select OCR languages and target language.
8. Click **실시간 번역 시작**.
9. Use **번역 기록** to inspect all results later.

## Game overlay appearance

The translucent click-through overlay drawn on top of the target game is independent from the separate movable live-translation window.

Open **설정 / ROI → 게임 오버레이 위치/크기/투명도** and select an ROI. Each ROI stores:

- horizontal / vertical offset from the automatic placement,
- overlay width scale,
- background opacity,
- translation font size,
- whether to show the OCR source,
- whether to show provider/OCR/agreement metadata.

Changes are applied immediately while monitoring is running and are saved to `settings.json`.

The separate **실시간 번역 창 열기/다시 열기** button can recreate that window after it has been closed.

## Web translation result extraction

Translation websites regularly change their HTML. RoiLingo therefore does not depend on one CSS selector. Version 1.5.0 attempts, in order:

1. site-specific known selectors,
2. **painted text-node geometry**: score the actual text nodes rendered in the right translation pane instead of a parent element that may also contain language menus,
3. **layout-anchored extraction**: locate the source sentence and score target-language text in the mirrored/right translation pane,
4. visible rendered DOM snapshot scoring with translator-menu rejection,
5. generic visible DOM heuristic,
6. whole-page visible text scoring,
7. Chromium accessibility tree,
8. **WebView copy bridge**: intercept the exact string a site's Copy button sends to `navigator.clipboard.writeText`, without depending on the Windows clipboard,
9. optional Windows clipboard Copy-button fallback,
10. **rendered WebView OCR fallback**: if the translation is visibly on screen but the DOM is unreadable, capture the right pane and OCR the displayed target text with Tesseract.

Version 1.5.0 rejects common false positives such as language menus (`한국어 / 영어 / 일본어 / 중국어 ...`) at both provider and cache boundaries. It also uses `translation-cache-hybrid-v3.json`, so incorrect results cached by older extractors are not reused. Use **번역 캐시 비우기** at any time to force the next sentence through the translators again.

Use **번역 결과 읽기 테스트** before live monitoring if a web provider visually translates but RoiLingo does not show the result. The runtime log now identifies methods such as `text-node-geometry`, `copy-bridge`, `copy-button-clipboard`, and `webview-visual-ocr`.

## API / local translation

### Papago API

Requires NAVER Cloud Papago Text Translation Client ID and Client Secret.

### Google API

Uses Cloud Translation Basic v2 with an API key. The key is sent using the `X-goog-api-key` request header instead of a URL query parameter.

### DeepL API

Supports API Free and Pro endpoints. During **API/로컬 연결 테스트**, RoiLingo also attempts to read DeepL character usage/limit from `/v2/usage`.

### LibreTranslate / Argos Translate

LibreTranslate is free/open-source and can run locally. RoiLingo defaults its endpoint field to:

```text
http://localhost:5000
```

If Docker Desktop is installed, run:

```bat
scripts\libretranslate-local.cmd
```

Then enable **LibreTranslate** in RoiLingo. A self-hosted local instance normally does not need an API key.

## Quota / credit rotation

Not every translation service exposes quota information through the same API, so RoiLingo uses two layers:

- **local limits** for every API/local provider: daily request count and monthly source-character count;
- **provider-specific actual usage** where practical: currently DeepL usage is queried during the connection test.

`0` means unlimited for a local limit. In Hybrid Balanced mode, API/local providers rotate, which helps avoid spending every provider's quota at the same time. Cache hits consume no translation request.

Local usage counters are saved in:

```text
%LocalAppData%\RobloxLiveTranslator\api-usage.json
```

Resetting this file/counter does **not** reset a provider's real billing or server-side quota.

## Secrets and privacy

API credentials are stored in:

```text
%LocalAppData%\RobloxLiveTranslator\api-secrets.dpapi
```

The file is encrypted with Windows DPAPI `CurrentUser`. Secrets are not written to `settings.json`, source code, Git commits, logs, or GitHub Releases.

OCR text is sent only to providers that you enable. Web providers receive text through their normal public translation page. API providers receive text through their configured API endpoint. A local LibreTranslate server can keep translation traffic on the local machine.

## Automatic history and logs

Existing RoiLingo/RobloxLiveTranslator settings remain under:

```text
%LocalAppData%\RobloxLiveTranslator\
```

Important files:

```text
settings.json
api-secrets.dpapi
api-usage.json
translation-cache-hybrid-v3.json
webview2\
history\translations-YYYY-MM-DD.jsonl
logs\runtime-YYYY-MM-DD.log
exports\translations-*.csv
```

## CPU usage and stale-queue strategy

RoiLingo does not run OCR on every frame. It captures the target window once per cycle, crops each ROI, computes a small signature, skips unchanged regions, waits for changed text to settle, and only then runs OCR. `Accurate` OCR mode performs extra Tesseract segmentation passes and therefore costs more CPU than `Fast` or `Balanced`.

Translation is not awaited inside the ROI capture loop. Each ROI owns one coalescing translation worker and a monotonically increasing revision. If OCR changes while a web translation is already running, RoiLingo does **not** interrupt the browser mid-navigation; the old result is discarded when it finishes, and only the newest pending sentence is translated next. The pending queue is therefore bounded to one item per ROI, avoiding both stale backlogs and the v1.5 failure mode where repeated cancellation left Papago/Google/DeepL with an empty source editor.

## GitHub publish

Repository name: **RoiLingo**

Recommended description:

> Windows ROI live OCR translator with Tesseract, WebView translators, official APIs, and LibreTranslate/Argos support.

Run:

```bat
github-bootstrap.cmd
```

The publisher:

- checks Git, .NET SDK, GitHub CLI and login,
- verifies XAML / restore / Release x64 build,
- initializes Git if needed,
- safely handles a repository that has **no first commit yet**,
- creates `RoiLingo` if missing,
- updates About/Topics,
- commits and pushes `main`,
- waits for the Windows GitHub Actions build when available,
- builds the Windows release zip,
- creates/pushes `v1.6.1`,
- creates the GitHub Release and uploads `RoiLingo-win-x64.zip`.

Version 1.3.0+ fixes the previous `fatal: Needed a single revision` failure: a brand-new repository is no longer probed with `git rev-parse --verify HEAD` under PowerShell's terminating error mode.

## Source layout

```text
src/RobloxLiveTranslator/
├── Models/
├── Monitoring/
├── Native/
├── Overlay/
├── Services/
├── Translation/
│   ├── Api/
│   └── Web/
├── MainWindow.*
└── RoiEditorWindow.*
```

## License

See `LICENSE`.
