# RoiLingo 2.1.1

## Fixed: Start no longer depends on opening Advanced Settings

### Root cause
The WebView2 controls used by the web translation providers were placed inside the collapsible **Advanced Settings** panel. The translation provider lazily initialized those same controls. On some WPF/WebView2 environments, opening the settings panel materialized the WebView2 visual/Win32 host and made translation work, while pressing **Start** in compact mode could leave the browser controls unready.

This created an invalid dependency:

```text
Start -> web translation -> WebView2 inside hidden settings UI
```

Translation must never depend on a diagnostics/settings surface being opened.

### Change
v2.1.1 adds a dedicated `WebTranslatorHostWindow` for the translation engine.

- It is modeless, off-screen, not shown in the taskbar, and does not take focus.
- Papago / Google / DeepL engine WebViews live there for the lifetime of the app.
- The host is warmed in the background shortly after startup.
- Pressing **Start** explicitly verifies the engine host before monitoring begins.
- Advanced Settings WebViews are now only a manual preview/debug surface.
- API-only mode does not create the WebView host.
- The engine remains alive across Stop -> Start cycles, avoiding repeated first-translation initialization.

### Expected behavior
The normal flow is now:

```text
Run RoiLingo
-> choose target
-> set/edit ROI
-> press Start
-> OCR / web cross-check translation works
```

The user does **not** need to open Settings first.

### Diagnostics
When web mode is enabled, the runtime log should contain one or both of:

```text
WEBENG  백그라운드 웹 번역 엔진 준비 완료
WEBENG  시작 버튼에서 웹 번역 엔진 준비 확인
```

If WebView2 Runtime is unavailable, Start reports that directly instead of silently waiting for the settings panel.
