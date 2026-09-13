# RoiLingo 1.1.0

## User-requested changes

- Replaced the default API-key translation workflow with embedded WebView2 translator tabs.
- Added Papago Web, Google Translate Web, and DeepL Web providers.
- Added parallel browser translation and cross-check result collection.
- Added persistent WebView2 profile/cookies so site preferences survive restarts.
- Added automatic translation history persistence (daily JSONL).
- Added automatic runtime logs (daily log file).
- Added searchable translation history UI with per-provider detail list.
- Added CSV export and one-click log folder access.
- Preserved 1.0.x ROI/settings data via settings schema migration.
- Namespaced the new browser translation cache so old API cache entries are not reused.
- Renamed the product/repository branding to RoiLingo while preserving the existing solution/project namespace for low-risk compatibility.
- Removed `global.json` SDK pinning to avoid machine-specific SDK resolution failures.
- Replaced the complex batch GitHub publisher with a tiny `.cmd` wrapper plus robust PowerShell implementation.
- GitHub publisher now chooses repository name `RoiLingo`, updates About/topics, builds, pushes, watches Actions, tags v1.1.0, and creates a Release.

## Important implementation note

Browser-based translation is intentionally inspectable: the translation websites remain available in visible tabs. Because those websites are third-party UI surfaces, their DOM can change. Site-specific URL/result logic is isolated in `Translation/Web/WebTranslationScripts.cs` so maintenance stays localized.
