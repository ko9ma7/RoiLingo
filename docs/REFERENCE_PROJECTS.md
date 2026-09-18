# Reference projects and adopted ideas

RoiLingo는 아래 공개 프로젝트를 기능/UX 참고 자료로 조사했습니다. **해당 저장소의 구현 코드를 복사해서 포함하지 않았습니다.** 라이선스가 명확하지 않은 프로젝트에서는 특히 아이디어 수준만 참고했습니다.

## Kushisusumita/screen-translator

Reference: https://github.com/Kushisusumita/screen-translator

License observed on repository: MIT.

Observed ideas:
- global hotkey
- frozen screenshot + drag region
- window/full-screen capture modes
- first successful translator can win for low latency
- tray/minimal UI
- optional clipboard

Adopted in RoiLingo 2.2:
- Ctrl+Alt+T region capture
- Ctrl+Alt+W active-window capture
- first-success low-latency path for one-shot translation

Not adopted yet:
- tray-only startup
- auto-update binary replacement

## OneMoreGres/ScreenTranslator

Reference: https://github.com/OneMoreGres/ScreenTranslator

License observed on repository: MIT.

Observed ideas:
- separate recognizer/translator resources
- hotkey capture workflow
- portable resources and update diagnostics
- modular translation backends

RoiLingo already has automatic Tesseract language-model acquisition and provider abstraction. `scripts/diagnose.cmd` was added to make resource problems easier to inspect.

## bigone2000/screen-select-translate

Reference: https://github.com/bigone2000/screen-select-translate

License was not confirmed during this review; RoiLingo therefore uses only general workflow ideas and no source code from this repository.

Observed ideas:
- OCR on images/video/Canvas/non-selectable content
- draggable result window
- persisted options
- UI internationalization

RoiLingo quick-region capture now covers the same non-selectable-screen use case outside the browser, while keeping the fixed-ROI live monitor.

## zixload/ocr-translate-overlay

Reference: https://github.com/zixload/ocr-translate-overlay

License was not confirmed during this review; no source code is copied.

Observed ideas:
- local OCR
- floating, resizable panels
- remembered window position/size
- automatic OCR model preparation

RoiLingo already uses local Tesseract OCR, model auto-download, persistent overlay geometry and history.

## meangrinch/MangaTranslator

Reference: https://github.com/meangrinch/MangaTranslator

License observed on repository: Apache-2.0. RoiLingo does not bundle its ML models or code.

Observed ideas:
- multiple OCR / translation backends
- local or cloud models
- multi-language support
- batch/media workflows

RoiLingo keeps backend boundaries so a future PaddleOCR/MangaOCR backend or batch-image workflow can be added without rewriting capture/overlay/history.
