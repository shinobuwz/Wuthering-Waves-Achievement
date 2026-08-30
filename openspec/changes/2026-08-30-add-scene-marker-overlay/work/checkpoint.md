# Checkpoint

## Status

Implementation and verification complete; ready for Commit A.

## Completed

- Added pure scene-marker ROI mapping, normalized regions, stride-aware BGR cropping, and identifier validation.
- Added exe-relative scene marker persistence with unique PNG/JSON pairs, scene-destination probing, cancellation cleanup, structural PNG validation, CRC checks, dimension checks, and explicit hashes.
- Added a frozen topmost WPF overlay with drag selection, crop preview, redraw, identifier input, save, Escape cancellation, save-time close protection, native positioning, and automation metadata.
- Added a Debug-default / Release-opt-in entry point using existing game discovery and capture.
- Main and map overlays are suspended during capture and restored afterward; map hotkey toggles are blocked for the marker session.
- Added focused tests and completed managed/Debug/Release verification.
- Completed a real 1920×1080 game smoke and user confirmation.

## Decisions

- Direct game-position overlay uses a frozen captured frame rather than a live transparent underlay.
- First version is capture-only; no Native matcher or scene configuration editing.
- Default output remains `<exe>/scene-marker-lab`; no silent LocalAppData fallback.
- The actual scene subdirectory is prepared before save so fallback also covers an occupied/unwritable scene path.
- Persistence derives marker BGR from source frame + ROI and treats PNG bytes as a separately validated/hash-recorded representation.

## Residuals

- Mixed-DPI multi-monitor smoke is deferred to broader application DPI work.
- Manual permission-denied picker smoke is deferred; deterministic destination-failure tests are present.
