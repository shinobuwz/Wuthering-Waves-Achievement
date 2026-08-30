# Task 02 — Build the frozen game overlay

## Requirements

- Add a topmost borderless WPF overlay displaying an immutable BGR capture.
- Position it over the game client in physical pixels.
- Support drag selection, redraw, crop preview, identifier input, save, and Escape cancellation.
- Keep selected and saved pixels sourced from the same frozen frame.
- Prompt for another directory only if `<exe>/scene-marker-lab` cannot be written.

## Acceptance

- Invalid or tiny selections cannot save.
- Cancelling creates no output.
- Successful save reports both output paths.
