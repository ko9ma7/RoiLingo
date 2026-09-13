# RoiLingo 1.3.0

## P0 fixes

- Fixed `github-bootstrap.cmd` / `github-publish.ps1` failing on a brand-new Git repository with `fatal: Needed a single revision`.
- Removed the failing `git rev-parse --verify HEAD` probe from the new-repository path. The script now checks Git refs without intentionally invoking a failing Git command under `$ErrorActionPreference = "Stop"`.
- Wrapped expected non-zero GitHub/Git probes in a non-terminating helper so repository/tag/release existence checks do not abort publishing.

## Web translation result extraction

- Added rendered visible-DOM snapshot extraction.
- Added whole-page visible body-text extraction.
- Retained known selector, generic DOM, and Chromium accessibility-tree fallbacks.
- Added optional target-side Copy-button/clipboard fallback.
- Clipboard fallback attempts to restore the user's previous clipboard contents after extraction.
- Increased default web timeout for migrated settings to 8 seconds.

## Hybrid web + API translation

Added official API/local providers:

- Papago Text Translation API
- Google Cloud Translation Basic v2 API
- DeepL API Free/Pro
- LibreTranslate / Argos Translate compatible endpoint

Added strategies:

- Hybrid Balanced (API/local 1 + Web 1, rotating)
- Web only
- API/local only
- Maximum cross-check

## Quota / credits

- Added local per-provider daily request limits.
- Added local per-provider monthly source-character limits.
- Added rotating API/local selection in balanced modes.
- Providers that reached a configured local budget are excluded before scheduling, so the next available provider can be used instead.
- Added persistent `api-usage.json` counters.
- Added DeepL actual `/v2/usage` query during API connection test.
- Cache hits do not consume a new translation request.

## Security

- API credentials are stored with Windows DPAPI CurrentUser encryption.
- Secrets are not written to settings.json or the repository.

## OCR

- Tesseract remains the default open-source OCR engine.
- Added Fast / Balanced / Accurate modes.
- Balanced and Accurate modes compare multiple Tesseract page segmentation passes.

## Local open-source translation

- Added `scripts/libretranslate-local.cmd` to start a local LibreTranslate Docker container on `127.0.0.1:5000` when Docker Desktop is installed.
