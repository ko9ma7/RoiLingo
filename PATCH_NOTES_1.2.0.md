# RoiLingo 1.2.0

## Fixed: web translator result was visible but not collected

The 1.1.0 browser adapter relied too heavily on old site-specific DOM selectors such as Papago's legacy `#txtTarget` / `#targetEditArea`. The current Papago page can display a translated sentence correctly while those selectors return nothing, which caused RoiLingo to report:

```text
[활성화된 번역 웹 사이트에서 결과를 읽지 못했습니다]
```

1.2.0 uses a layered result reader:

1. known site-specific selectors (fast path),
2. visible DOM heuristic scoring,
3. the translator site's Copy button as a semantic anchor for nearby result text,
4. Chromium/WebView2 accessibility tree fallback via DevTools Protocol.

The application deliberately does **not** overwrite the Windows clipboard during automatic translation. Programmatically clicking a site's Copy button can be rejected as an untrusted browser gesture and would also replace whatever the user currently has on the clipboard. Instead, RoiLingo uses the Copy button as a stable semantic anchor and reads the neighboring translated text directly.

The runtime log now records successful extraction methods, for example:

```text
WEBREAD Papago Web: 결과 읽기 성공 (dom-heuristic, 1430ms)
WEBREAD Google Web: 결과 읽기 성공 (known-selector, 820ms)
```

A new **번역 결과 읽기 테스트** button runs a controlled test sentence through the enabled browser translators and shows per-provider success/failure without starting ROI monitoring.

## Fixed: cross-check window could expire before first web result

When a translation website was slow on its first request, the previous cross-check timer could stop waiting before the provider's own timeout expired. 1.2.0 keeps the fast cross-check window when results exist, but gives a longer hard deadline when *no* provider has returned a result yet.

## Added: movable/resizable live translation window

A new floating **실시간 번역 창** can be enabled from Settings / ROI.

- normal Windows window: drag the title bar to move it,
- resize from edges/corners,
- always-on-top toggle,
- source-text visibility toggle,
- adjustable translation font size,
- rolling recent translation list,
- position, size and display options are automatically saved and restored.

The existing click-through overlay remains available, so users can use either or both.

## Fixed: GitHub bootstrap XAML verification on Windows PowerShell 5.1

`verify.ps1` previously used `Get-Content -Raw` without an explicit encoding. Windows PowerShell 5.1 can interpret UTF-8-without-BOM XAML as the system ANSI code page. In Korean XAML this can consume quote bytes as part of a DBCS sequence, making otherwise valid XML appear malformed.

The verifier now reads XAML bytes explicitly as strict UTF-8 with `System.IO.File.ReadAllText(..., UTF8Encoding)` before XML parsing. PowerShell scripts are also shipped with a UTF-8 BOM for Windows PowerShell 5.1 compatibility.

This is the root cause of the GitHub publish log that failed during:

```text
[CHECK] XAML well-formedness
...
'<', hexadecimal value 0x3C, is an invalid attribute character
```

The GitHub publisher remains idempotent and now targets release tag `v1.2.0`.
