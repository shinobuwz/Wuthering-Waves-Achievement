# Task 03 — Wire the internal entry point and verify

## Requirements

- Add a Debug-visible OCR-area entry point.
- Enable the same entry point in Release only through `WUWA_SCENE_MARKER_LAB`.
- Use existing Wuthering Waves process discovery and window capture.
- Hide the main window before capture and restore it on save, cancellation, and failure.
- Log failures without changing OCR/workspace state.
- Document usage, storage, output schema, and manual smoke steps.

## Acceptance

- Focused and full managed tests pass.
- Release build succeeds with zero warnings and errors.
- Evidence records the available automated and manual verification.
