# Scene transition core

`SceneTransitionEngine<TFrame>` provides the reusable scene-switching portion of
an ImageHandle-style recognition pipeline. It deliberately contains no WPF,
OpenCV, OCR, persistence, or Wuthering Waves scene definitions.

## Included behavior

- ordered transition matrices;
- first-match-wins candidate evaluation;
- configurable confirmation thresholds for known and unknown scenes;
- explicit optional Handler registration;
- Handler invocation for every real scene match;
- synthetic unknown fallback when no candidate matches;
- serialized frame processing, cancellation, and asynchronous reset;
- immutable per-frame transition results.

## Minimal use

```csharp
var options = new SceneTransitionOptions(
    initialScene: "unknown",
    unknownScene: "unknown",
    transitionMatrix: new Dictionary<string, IReadOnlyList<string>>
    {
        ["unknown"] = ["achievement-list", "loading"],
        ["achievement-list"] = ["achievement-list", "loading"],
        ["loading"] = ["loading", "achievement-list"]
    });

ISceneMatcher<MyFrame> matcher = new MySceneMatcher();
var handlers = new Dictionary<string, ISceneHandler<MyFrame>>
{
    ["achievement-list"] = new AchievementListHandler()
};

using var engine = new SceneTransitionEngine<MyFrame>(options, matcher, handlers);
var result = await engine.ProcessAsync(frame, cancellationToken);
await engine.ResetAsync(cancellationToken);
await engine.ResetAsync("achievement-list", cancellationToken);
```

The matcher is called sequentially in configured candidate order. Evaluation
stops after the first match. A registered Handler receives the frame, the
stable scene before and after the observation, the original match, and whether
that frame confirmed a transition. An unregistered scene still follows the
generic matching and transition path.

If no candidate matches, the engine emits a synthetic unknown observation for
transition debouncing. Synthetic unknown does not invoke a Handler because it
is not a real matcher hit.

`ProcessAsync` and `ResetAsync` share one ordered queue. Cancellation does not
allow later work to overtake an unfinished predecessor. A matcher or Handler
cannot enqueue another operation on the same engine; reentrant calls fail
immediately instead of waiting on the frame that invoked the callback.

## Current boundary

This first change does **not** include:

- scene templates, ROI values, or thresholds;
- a Native/OpenCV `ISceneMatcher<OcrImageFrame>` adapter;
- Wuthering Waves scene ids or Handlers;
- a capture or polling loop;
- WPF/OCR workbench integration;
- achievement progress updates.

A later Infrastructure change can adapt the existing `OcrImageFrame`, Native
OpenCV matcher, and `WindowsGameWindowCapture` to these interfaces. Scene
recognition must not write progress directly: OCR results still become an
`OcrScanPreview` and are applied only through
`AchievementWorkspace.ApplyOcrPreviewAsync` after explicit confirmation.

## Internal scene marker lab

The WPF app also contains a capture-only test tool for preparing future scene
templates. In Debug builds, open the modular shell's **游戏工具** page and use
**DEBUG：采集场景标记**.
In Release builds, explicitly opt in before starting the app:

```powershell
$env:WUWA_SCENE_MARKER_LAB = "1"
```

The tool finds the game window, temporarily hides any active map overlay and
the main app, waits briefly for desktop composition to settle, captures the game
client, and displays that frozen frame in a topmost borderless overlay positioned
over the same physical screen rectangle. Map hotkey toggles are blocked during
the marker session. The main app and prior map-overlay state are restored after
save, cancellation, or failure. Drag
a rectangle, enter lowercase `Scene ID` and `Marker`
identifiers, inspect the exact BGR crop, and save it. Escape or Cancel closes
the overlay without creating files.

Captures are written below the executable by default:

```text
<exe>/scene-marker-lab/<scene-id>/
  <timestamp>-<marker>-<hash>-<unique>.png
  <timestamp>-<marker>-<hash>-<unique>.json
```

If the executable directory cannot be written, the tool reports the error and
opens a folder picker. It never silently falls back to LocalAppData. The JSON
records schema version, source dimensions and stride, process/window context,
physical client bounds, pixel and normalized ROI, capture time, and separate
SHA-256 hashes for source BGR, cropped marker BGR, and exact PNG bytes. PNG
chunks, CRCs, and dimensions are validated before commit. This tool only
acquires marker fixtures: it does not run a scene
matcher, edit production scene configuration, write repository resources, or
change OCR/workspace state.
