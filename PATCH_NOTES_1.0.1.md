# Patch Notes 1.0.1

## Fixed

- CS0246 `HttpClient` in `ModelManager.cs` and translation providers.
- CS0246 `HttpRequestMessage` / CS0103 `HttpMethod` in Papago/DeepL providers.
- Removed dependency on SDK-generated implicit usings by adding `GlobalUsings.cs` and disabling implicit usings.
- IDE0290 reduced to silent because it is a style suggestion, not a compiler failure.

## Reliability

- Long-lived static `HttpClient` per provider/model downloader.
- OCR model download uses a temporary file, validates minimum size, then atomically replaces the final file.
- Translation provider exceptions are surfaced to the application log instead of being silently swallowed.

## Build / Release

- Added deterministic .NET 8 `global.json`.
- Added `.editorconfig`, `.gitattributes`, expanded `.gitignore`, MIT `LICENSE`.
- Added `scripts/verify.cmd` / `verify.ps1` for restore + Release/x64 build verification.
- Improved self-contained release build script.
- Added Windows GitHub Actions build artifact workflow.
- Added idempotent `github-bootstrap.cmd` for repo creation, push, Actions verification, tag, and GitHub Release.
- GitHub Pages is intentionally not enabled because this is a Windows desktop application.

## Local verification in this delivery environment

Performed static checks for:

- all XAML / csproj XML well-formedness
- XAML event-handler references
- GitHub Actions YAML parse
- explicit `System.Net.Http` imports at direct HTTP call sites
- basic C# delimiter/syntax balance
- obvious hard-coded secret patterns

A Windows/.NET WPF compiler is not available in the delivery container, so the authoritative compile check is `scripts\\verify.cmd` on Windows or the included GitHub Actions workflow.
