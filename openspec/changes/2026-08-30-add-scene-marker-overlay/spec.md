# Change: Add scene marker capture overlay

## Intent

Add an internal Windows/WPF testing tool that captures the current Wuthering Waves client area, places a frozen screenshot overlay at the exact game-client screen bounds, lets a tester drag-select a marker rectangle, previews the crop, and saves a PNG plus JSON metadata for later scene-matcher work.

## Scope

### In scope

- A Debug-by-default, Release-opt-in entry point controlled by `WUWA_SCENE_MARKER_LAB`.
- Discovery and capture through the existing `WindowsGameWindowCapture` boundary.
- Automatic hiding of the main app and any active map overlay before capture, with restoration after the marker overlay closes.
- A borderless topmost WPF overlay positioned in physical pixels over the game client.
- A frozen screenshot so the selected pixels and saved pixels are identical.
- Drag selection in either direction, clamping, minimum-size validation, redraw, Escape cancellation, and crop preview.
- Scene ID and marker name validation.
- Packed BGR cropping independent of WPF and covered by unit tests.
- PNG and versioned JSON metadata persistence.
- Default storage under `<exe>/scene-marker-lab`; if that directory is not writable, show an explicit folder picker and do not silently fall back to LocalAppData.
- Diagnostic logging and documentation of the marker-lab boundary.

### Out of scope

- Generic Native/OpenCV scene matching.
- Matcher confidence testing or scanning a frame for the captured marker.
- Production scene definitions, transition-matrix editing, or OCR workflow integration.
- Direct writes to repository `resources` or achievement progress.
- A continuously transparent/live overlay.

## Behavior contract

1. The entry point is visible in Debug builds. In non-Debug builds it is visible only when `WUWA_SCENE_MARKER_LAB` is one of `1`, `true`, `yes`, or `on` (case-insensitive).
2. Starting capture finds a visible Wuthering Waves window of at least 800×600 and obtains its physical client bounds. The marker session blocks map hotkey toggles, temporarily hides any active map overlay and the main app, then waits for desktop composition to settle before capture.
3. Capture is rejected if any side of the game client rectangle changes during acquisition. The frozen overlay is positioned with checked native `SetWindowPos` over that exact rectangle. When the overlay closes or capture fails, the main app and prior map-overlay state are restored before returning to normal operation.
4. Pointer coordinates are mapped from the actual WPF selection surface to source-frame pixels. Reverse drags and out-of-bounds points are normalized and clamped.
5. A selection smaller than 3×3 source pixels is rejected without producing output.
6. Saving requires lowercase scene and marker identifiers using letters, digits, `.`, `_`, and `-`, beginning with a letter or digit.
7. Each save creates a structurally validated marker PNG and adjacent JSON metadata. PNG chunk CRCs and dimensions must be valid. Metadata includes schema version, identifiers, capture time, process/window context, source dimensions, pixel ROI, normalized ROI, and separate SHA-256 hashes for the source BGR frame, marker BGR crop, and exact PNG bytes.
8. Saves use unique names, temporary files, and cancellation checks before commit so a failed write does not silently replace an existing capture or leave a one-sided pair.
9. The normal output root is `AppPaths.ApplicationDirectory/scene-marker-lab`. The actual `<root>/<scene-id>` destination is probed; if it cannot be written, the UI reports the reason and asks the tester to choose another directory.
10. Closing is deferred while a save is in progress. Normal closing or cancellation creates no files and does not modify OCR previews, workspace revisions, progress, or scene-engine state.

## Verification

- Unit tests cover display-to-source mapping, reverse drag, clamping, minimum-size rejection, stride-aware BGR crop, normalized ROI, identifier validation, scene-destination probing, real PNG structure/CRC/dimensions, cancellation cleanup, storage naming, JSON contents, and non-overwrite behavior.
- Release build succeeds without warnings.
- Full managed test suite remains green.
- A Windows manual smoke verifies 1920×1080 game-window alignment, automatic main-window hiding/restoration, drag selection, preview, and default exe-relative PNG/JSON output. Mixed-DPI and permission-denied folder fallback remain targeted follow-up smoke cases.
