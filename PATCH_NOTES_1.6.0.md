# RoiLingo 1.6.0

## Fixed: OCR worked but web translator stayed empty

v1.5 cancelled the per-ROI translation task immediately whenever a newer OCR string appeared. With noisy/scrolling OCR this could repeatedly cancel WebView2 while Papago/Google/DeepL was still navigating or populating its source editor. The visible result was exactly the reported symptom: OCR changed, but the translator page remained blank and no translated text came back.

1.6 changes the pipeline to a bounded coalescing worker per ROI:

- the currently running browser translation is not interrupted mid-input,
- a newer OCR sentence replaces the single pending item,
- stale completed results are discarded using the ROI revision,
- the pending queue cannot grow without bound.

WebView translation also now verifies that the source editor contains the OCR text. When the site's deep-link query string does not populate the editor, RoiLingo injects the text into the visible source control and dispatches `input`/`change` events. Diagnostics log `원문 전달 확인` or `원문 재전달 성공/실패`.

## Fixed: the small in-game overlay itself can now be moved/resized

The separate live translation window was not what was requested. 1.6 adds direct edit mode for the actual translucent in-game ROI overlay.

While live translation is running, click **게임 오버레이 직접 편집**:

- drag a translation box to move it,
- drag the lower-right blue handle to resize width/font,
- mouse wheel adjusts width,
- Ctrl + mouse wheel adjusts opacity,
- Shift + mouse wheel adjusts font size,
- click the same button again to return to click-through mode.

Per-ROI values continue to be saved in the existing settings model. The previous detailed settings window is kept as an alternate precise editor.

## Fixed: GitHub `main -> main (fetch first)`

Earlier bootstrap attempts could leave a `ko9ma7/RoiLingo` remote repository with commits while a newly extracted local folder started from a new root commit. A normal push was then correctly rejected by GitHub.

1.6 now:

1. fetches `origin/main`,
2. detects an existing remote branch,
3. rebases the complete current release snapshot on top of the remote history using a normal sync commit,
4. pushes without force.

This preserves existing remote history and makes repeated bootstrap runs idempotent. About/Topics update errors are warnings instead of falsely printing success.

## Compatibility

- Target framework remains .NET 8 Windows/WPF.
- Existing ROI/settings/history are retained.
- API/local translation providers are unchanged.
- Existing web result extraction and copy/clipboard/visual-OCR fallbacks are retained.
