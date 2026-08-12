# Native OCR Scan

## Intent

Replace the Python-only OCR runtime with a Windows x64 C++ engine that the WPF application can call through a stable C ABI, while keeping the existing Python scan workflow available until native parity is proven.

## Scope

### In scope

- `Wuwa.Ocr.Native.dll`, built by Visual Studio 2022/CMake as C++20 x64.
- PP-OCRv5 detector, optional angle classifier, and recognizer using ONNX Runtime CPU execution.
- OpenCV image operations and Clipper2 DB-box expansion in the detector postprocessor.
- A managed safe-handle adapter; native errors never cross the ABI as exceptions.
- Windows game-window capture, single-page/global scan orchestration, cancellation, preview, fuzzy matching, and transactional progress merge through `AchievementWorkspace`.
- Side-by-side model reuse from the existing immutable `onnxocr/models/ppocrv5` files during development and copied model assets during publish.

### Non-goals

- Deleting or changing Python OCR before native parity and release verification.
- CUDA/DirectML/TensorRT in the first native release.
- Tracker overlay behavior, which remains the following change.
- Writing directly to legacy progress files.

## Observable behavior

1. Native OCR initialization validates ABI version, all model paths, recognition dictionary/class count, and model input/output shapes before accepting work.
2. OCR accepts packed BGR buffers with explicit width, height, and stride; invalid buffers produce structured failures rather than process termination.
3. Recognition preprocessing and CTC decoding match the existing PP-OCRv5 Python settings: `3x48xDynamicWidth`, normalization to `[-1,1]`, blank index `0`, duplicate collapse, UTF-8 dictionary entries, and optional space class.
4. Full OCR returns ordered text boxes, UTF-8 text, and confidence. Detector DB thresholds and unclip behavior are configuration, not hidden constants.
5. Native handles own all result memory; returned pointers remain valid until the next call on that handle or handle destruction. One handle serializes inference calls.
6. The scan layer never updates progress until the user accepts a preview. Accepted results merge through `AchievementWorkspace` into a new generation; cancellation or failure leaves the current revision unchanged.
7. The Python app and files remain untouched and runnable side-by-side.

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
- Later differential fixtures compare Python and C++ OCR on captured crops and full screenshots.
- Release verification builds the C++ DLL, .NET tests/build, and self-contained `win-x64` package containing native dependencies and model assets.
