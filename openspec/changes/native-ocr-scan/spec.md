# Native OCR Scan

## Intent

Make the Native WPF application the only maintained OCR runtime, using a Windows x64 C++ engine exposed through a stable C ABI.

## Scope

### In scope

- `Wuwa.Ocr.Native.dll`, built by Visual Studio 2022/CMake as C++20 x64.
- PP-OCRv5 detector, optional angle classifier, and recognizer using ONNX Runtime CPU execution.
- OpenCV image operations and Clipper2 DB-box expansion in the detector postprocessor.
- A managed safe-handle adapter; native errors never cross the ABI as exceptions.
- Windows game-window capture, current-category and full-scan orchestration, cancellation, preview, fuzzy matching, and transactional progress merge through `AchievementWorkspace`.
- Reuse of immutable Native OCR model assets during development and copied model assets during publish.

### Non-goals

- Tracker overlay behavior, which remains the following change.
- CUDA/DirectML/TensorRT in the first native release.
- Writing directly to legacy progress files.
- Adding a second top-level application tab or changing achievement/group data semantics for full scanning.

## Observable behavior

1. Native OCR initialization validates ABI version, all model paths, recognition dictionary/class count, and model input/output shapes before accepting work.
2. OCR accepts packed BGR buffers with explicit width, height, and stride; invalid buffers produce structured failures rather than process termination.
3. Recognition preprocessing and CTC decoding use the PP-OCRv5 contract: `3x48xDynamicWidth`, normalization to `[-1,1]`, blank index `0`, duplicate collapse, UTF-8 dictionary entries, and optional space class.
4. Full OCR returns ordered text boxes, UTF-8 text, and confidence. Detector DB thresholds and unclip behavior are configuration, not hidden constants.
5. Native handles own all result memory; returned pointers remain valid until the next call on that handle or handle destruction. One handle serializes inference calls.
6. The scan layer never updates progress until the user accepts a preview. Accepted results merge through `AchievementWorkspace` into a new generation; cancellation or failure leaves the current revision unchanged.
7. The repository contains only the Native application runtime; legacy profile files remain read-only migration inputs.
8. Native exposes two scan modes: the existing current-category scan and a separate full-scan command. The latter does not replace or change the former.
9. Full scan follows the shipped primary-category order and defined safeguards: primary-tab selection and OCR verification, secondary-tab discovery/clicking/scrolling, per-secondary-category achievement-page scanning, bounded no-new-content termination, and progress callbacks.
10. Full-scan progress reports the active primary category, secondary category, category counts, page number, accumulated matched count, and recoverable skipped-tab warnings. Cancellation stops the run without activating a workspace revision.
11. Full-scan candidates are accumulated in memory and shown in one final preview. Accepted results use the existing `AchievementWorkspace` merge contract, including completed-status downgrade protection; failed or unvisited tabs are not silently treated as incomplete.

## Full-scan behavior

The Native full-scan command is a second command in the existing OCR surface, not a new application tab. It follows the shipped navigation workflow in this order:

```text
primary tabs (shipped order)
  -> click and OCR-verify primary tab
    -> discover visible secondary tabs against shipped category names
      -> click each unvisited secondary tab
        -> wait for list load
          -> template-match achievement icons and OCR name/status crops
            -> scroll the achievement list with the tested privilege-matched input adapter
              -> stop on repeated/empty page
        -> merge candidates in memory
  -> continue to the next primary tab
-> show one preview and apply only after explicit confirmation
```

Primary or secondary navigation failures are bounded and reported with the affected category. Results from successfully visited categories remain reviewable. The current-category command continues to use the existing page loop independently.

## Public behavior seam and verification surface

The highest public behavior seam for this extension is the Native OCR scan coordinator's full-run operation: it accepts scan options and cancellation, emits immutable progress/events, and returns either a complete preview/run report or a structured failure without mutating workspace state. Production Win32 capture/input and a fake navigation/capture adapter exercise the same coordinator contract. Tests assert observable category order, progress, cancellation, failure reporting, candidate merge, and no-write-before-preview behavior rather than private UI call order or Win32 implementation details.

## First vertical slice

The initial implementation establishes the buildable/tested native boundary and recognition stage:

- stable ABI v1;
- ONNX Runtime recognition session;
- dynamic-width BGR preprocessing;
- CTC decoder and confidence;
- C# safe-handle adapter;
- real-model smoke test.

Detector/OpenCV/Clipper2 and game capture follow as explicit later tasks on the same ABI.

## Verification

- C++ unit tests for dictionary/UTF-8, CTC blank/duplicate behavior, input validation, and error-buffer sizing.
- C++ real-model smoke against shipped `rec.onnx` and dictionary.
- Managed integration smoke crossing C# → C ABI → C++ → ONNX Runtime.
- Differential fixtures compare Native C++ OCR against captured crops and full screenshots.
- Release verification builds the C++ DLL, .NET tests/build, and self-contained `win-x64` package containing native dependencies and model assets.
