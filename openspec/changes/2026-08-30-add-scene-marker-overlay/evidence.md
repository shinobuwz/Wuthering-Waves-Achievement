# Evidence

## Automated verification

### Focused marker tests

Command:

```text
dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SceneMarkerCaptureTests
```

Result: 29 passed, 0 failed, 0 skipped.

Covered behavior includes display/source coordinate mapping, reverse drag, clamping, minimum ROI size, stride-aware BGR crop, normalized ROI, identifier policy, Debug/Release opt-in, root and scene-directory probing, real PNG structure/CRC/dimension validation, cancellation cleanup, unique filenames, JSON metadata, and temp-file cleanup.

### Full managed suite

Command:

```text
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
```

Result: 128 passed, 0 failed, 4 existing environment-dependent tests skipped.

### Release build

Command:

```text
dotnet build WutheringWavesAchievement.sln -c Release --no-restore
```

Result: succeeded with 0 warnings and 0 errors.

### Debug WPF compile

Command:

```text
dotnet build src/Wuwa.App/Wuwa.App.csproj -c Debug --no-restore -p:BuildNativeOcr=false
```

Result: succeeded with 0 warnings and 0 errors. This verifies the Debug-visible marker-lab entry path without rebuilding the Native OCR engine.

### Diff hygiene

Command:

```text
git diff --check
```

Result: no whitespace errors.

## Windows/game manual smoke

Environment:

- Windows game client discovered as `Client-Win64-Shipping`, visible client 1920×1080.
- Marker lab enabled in a Release-hosted smoke with `WUWA_SCENE_MARKER_LAB=1`.

Observed:

- The marker button was visible only through the explicit Release opt-in.
- Starting capture automatically hid the main app before acquiring the desktop-backed game frame.
- The frozen overlay aligned to the 1920×1080 game client.
- Drag selection produced a crop preview and editable Scene/Marker fields.
- Closing the overlay restored the main app.
- A real WPF-encoded PNG and adjacent JSON were saved under the executable-relative `scene-marker-lab/<scene-id>/` directory.
- The saved JSON recorded source 1920×1080/stride 5760, physical client bounds, pixel and normalized ROI, and three 64-character SHA-256 hashes.
- The WPF PNG passed the storage layer's signature, chunk-boundary, CRC, completeness, and dimension checks.

User confirmation after the main-window auto-hide fix: screenshot and interaction behavior tested successfully.

## Focused review

A parallel review covered correctness, architecture, and tests/UX. Accepted findings were addressed as follows:

- active map overlay contamination: marker sessions now suspend/restore the map overlay and block map hotkey toggles;
- placement failures: initial physical-as-DIP assignments were removed and `SetWindowPos` failures now throw;
- game movement: all four client bounds are checked across capture;
- Alt+F4 during save: closing is deferred while persistence is active;
- PNG/frame consistency: storage derives the BGR crop from source+ROI, validates complete PNG structure/CRC/dimensions, and records separate BGR/PNG hashes;
- scene-subdirectory fallback: the actual `<root>/<scene-id>` destination is probed before save;
- accessibility diagnostics: stable automation IDs/names and live status/error announcements were added.

## Residual verification notes

- Mixed-DPI multi-monitor alignment was not separately exercised in this change. Native placement now fails closed, but broader per-monitor DPI policy remains an application-level concern.
- Permission-denied folder-picker fallback was not forced in the manual game smoke; deterministic tests cover unwritable root and occupied scene-destination detection.
- Generic Native/OpenCV scene matching remains explicitly out of scope.
