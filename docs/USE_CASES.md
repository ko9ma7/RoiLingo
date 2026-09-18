# RoiLingo usage modes

## Game event / admin message monitor

Use a fixed ROI, mark it as Event, and Start. Brief messages are retained as snapshots and translated even if they disappear.

## Video subtitle monitor

Create one wide ROI over the subtitle area. Use a longer overlay hold time if subtitle transitions are too fast.

## PDF / image / locked webpage

Press `Ctrl+Alt+T`, drag the text area, and read the one-shot result. No fixed target window is required.

## Whole application window

Focus the application and press `Ctrl+Alt+W`. RoiLingo captures the client area and runs OCR + translation once.

## Text already copied

Press `Ctrl+Alt+V`. RoiLingo skips OCR and translates clipboard text directly.

## API-free mode

Use `무료 · Web 3종 교차검증` for fixed ROI. Quick one-shot uses the first validated result from enabled Web providers for lower latency.

## Deterministic automation mode

Configure Papago / Google / DeepL API or LibreTranslate and select `API/로컬만`. This avoids webpage DOM changes.
