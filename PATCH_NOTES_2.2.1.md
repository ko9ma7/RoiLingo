# RoiLingo 2.2.1

This is a runtime-reliability hotfix after auditing the 2.2.0 startup/capture/WebView pipeline.

## Root causes fixed

1. **Start could be blocked by WebView initialization.**
   2.2.0 waited for every enabled Web translator before starting the ROI monitor. If a WebView2 controller was slow or failed to materialize, capture/OCR never started.

2. **The hidden translator host used a TabControl.**
   The non-selected Google/DeepL WebView2 controls could remain unmaterialized until UI state changed.

3. **An unshown hidden host could be replaced before Loaded.**
   Translation providers hold direct references to WebView controls. The old factory treated `IsLoaded == false` as "no host" and could replace the host object during startup, leaving providers bound to orphaned WebViews. This race explains the symptom where opening settings or waiting changed behavior.

4. **Background-first capture could accept a bad PrintWindow frame for GPU games.**
   PrintWindow can return success with a blank/near-uniform/stale accelerated surface. The old code only rejected almost-black output, so a white/uniform frame could prevent the reliable foreground screen fallback.

## Changes

- ROI monitoring starts **before** WebView warm-up. OCR/event capture is no longer blocked by translation browser startup.
- Web providers initialize lazily on first use; background warm-up remains an optimization only.
- Hidden WebView host now uses one Grid with three non-zero rows; Papago, Google and DeepL stay materialized simultaneously.
- Each Web provider initializes independently. One site failing does not prevent the other sites from becoming available.
- Capture default changed to **Auto**:
  - foreground target: direct screen client capture first (best for games/GPU rendering)
  - covered/inactive target: PrintWindow background capture
  - BackgroundOnly remains available explicitly.
- PrintWindow false-success frames now reject almost-black, almost-white, and extremely uniform images.
- Runtime status reports the active capture method when it changes.
- Existing schema-10 installations migrate `BackgroundFirst` to `Auto` once.
- Translation cache bumped to `translation-cache-hybrid-v11.json`.

## Expected startup behavior

`Target -> ROI -> Start` should begin ROI capture immediately. Web translators may still take time to become ready, but captured OCR events stay in the normal translation queue instead of preventing monitor startup.
