# Patch Notes 1.0.2

## Fixed

- `global.json` SDK resolution failure on PCs without an installed 8.0 feature band.
- Changed SDK roll-forward policy from `latestFeature` to `major`.
- Project still requires **SDK 8.0.100 or later**, while allowing installed stable SDK 8.x / 9.x / 10.x.
- `scripts/verify.ps1` now validates the minimum SDK version instead of requiring the selected SDK to start with `8.`.
- `github-bootstrap.cmd` uses the same SDK compatibility rule.
- Project package version and release tag updated to `1.0.2` / `v1.0.2`.

## Behavior compatibility

- Target framework remains `net8.0-windows10.0.19041.0`.
- OCR, ROI, translation providers, overlay, settings and secrets behavior are unchanged.

## Verify on Windows

```bat
dotnet --list-sdks
dotnet --version
scripts\verify.cmd
```
