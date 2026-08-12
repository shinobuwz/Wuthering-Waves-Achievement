# Wuwa.Ocr.Native

`Wuwa.Ocr.Native.dll` is the Windows x64 C++ OCR boundary for the native WPF application. It owns PP-OCRv5 DB detection and recognition preprocessing, ONNX Runtime inference, OpenCV image operations, Clipper2 expansion, perspective crops, CTC decoding, UTF-8 result lifetime, structured errors, and a stable C ABI. It reads the existing models and dictionary without modifying anything under `onnxocr/`.

## Toolchain

- Visual Studio 2022 with **Desktop development with C++**
- CMake bundled with Visual Studio
- C++20 / MSVC x64
- `Microsoft.ML.OnnxRuntime` native assets restored from NuGet (pinned to `1.22.1`)
- OpenCV `4.12.0` Windows SDK downloaded with a pinned SHA-256
- Clipper2 C++ source vendored under `third_party/clipper2` with its Boost license

## Build and test

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/build-native-ocr.ps1 -Configuration Release
```

The command builds the DLL and runs:

- dictionary/UTF-8 and CTC decoder unit tests;
- real PP-OCRv5 `det.onnx` and `rec.onnx` session smoke using a synthetic BGR image;
- DB bitmap/contour/unclip/perspective-crop execution on the blank smoke image.

Outputs are written to `native/ocr/build/Release` and are intentionally ignored by Git.

## ABI

The public C ABI is declared in `include/wuwa_ocr.h`. `Wuwa.Infrastructure.NativeOcrClient` owns the native handle and serializes calls because one ONNX Runtime session/result buffer is shared per instance. Set `WUWA_NATIVE_OCR_ROOT` to a directory containing:

- `Wuwa.Ocr.Native.dll`
- `onnxruntime.dll`
- `onnxruntime_providers_shared.dll`
- `opencv_world4120.dll`

The packaged application will instead place these files in its `ocr/` directory.

## Current boundary

Implemented now:

- detector/recognizer model and dictionary validation;
- PP-OCRv5 DB detector preprocessing and postprocessing;
- OpenCV contours, rotated boxes, perspective crops, and reading-order sorting;
- Clipper2 box expansion;
- dynamic-width PP-OCRv5 recognition preprocessing;
- CPU ONNX Runtime sessions;
- CTC decoding and confidence;
- single-line and full-page UTF-8 C ABI plus managed safe-handle wrapper.

Still to implement in the same native DLL:

- optional angle classifier;
- multi-crop recognition batching;
- captured-image Python/C++ differential fixtures;
- game-window capture, scan orchestration, preview, cancellation, and workspace merge.

The Python OCR remains available side-by-side until native scan parity is verified.
