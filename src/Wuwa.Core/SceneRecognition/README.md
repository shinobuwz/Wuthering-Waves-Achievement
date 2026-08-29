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
