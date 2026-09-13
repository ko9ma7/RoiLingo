# RoiLingo 1.6.1

## Fixed

- Fixed `CS1524` in `Monitoring/MonitorEngine.cs`. An outer `try` block left behind during the 1.6.0 translation-worker refactor had no matching `catch` or `finally`.
- Kept the per-ROI latest-wins translation behavior while removing only the invalid outer `try`. Per-translation cancellation/error handling remains in place.
- Simplified `_states` initialization to the C# 12 collection expression (`[]`) so Visual Studio no longer suggests IDE0028 for that field.
- Bumped the GitHub publisher/release tag to `v1.6.1`.

## Why the CMD publisher failed

`github-bootstrap.cmd` intentionally runs `scripts\verify.ps1` before touching GitHub. Because the project did not compile, publishing stopped at the build gate. The CMD itself was not the cause of `CS1524`. After this source fix the publisher can continue to repository synchronization/push.

## Verify

Run:

```bat
scripts\verify.cmd
```

Expected:

```text
[OK] XAML parse
[OK] restore
[OK] build
[OK] Verification completed.
```

Then run:

```bat
github-bootstrap.cmd
```
