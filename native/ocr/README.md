# Wuwa.Ocr.Native

`Wuwa.Ocr.Native.dll` is the Windows x64 C++ OCR boundary for the native WPF application. The first vertical slice owns PP-OCRv5 recognition preprocessing, ONNX Runtime inference, CTC decoding, UTF-8 result lifetime, structured errors, and a stable C ABI. It reads the existing recognition model and dictionary without modifying anything under `onnxocr/`.

## Toolchain

- Visual Studio 2022 with **Desktop development with C++**
- CMake bundled with Visual Studio
- C++20 / MSVC x64
- `Microsoft.ML.OnnxRuntime` native assets restored from NuGet (pinned to `1.22.1` by the build script)

## Build and test

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/build-native-ocr.ps1 -Configuration Release
```

The command builds the DLL and runs:

- dictionary/UTF-8 and CTC decoder unit tests;
- a real PP-OCRv5 `rec.onnx` session smoke using a synthetic BGR image.

Outputs are written to `native/ocr/build/Release` and are intentionally ignored by Git.

## ABI

The public C ABI is declared in `include/wuwa_ocr.h`. `Wuwa.Infrastructure.NativeOcrClient` owns the native handle and serializes calls because one ONNX Runtime session/result buffer is shared per instance. Set `WUWA_NATIVE_OCR_ROOT` to a directory containing:

- `Wuwa.Ocr.Native.dll`
- `onnxruntime.dll`
- `onnxruntime_providers_shared.dll`

The packaged application will instead place these files in its `ocr/` directory.

## Current boundary

Implemented now:

- model/dictionary validation;
- dynamic-width PP-OCRv5 recognition preprocessing;
- CPU ONNX Runtime session;
- CTC decoding and confidence;
- stable UTF-8 C ABI and managed safe-handle wrapper.

Still to implement in the same native DLL:

- PP-OCRv5 DB detection preprocessing/postprocessing with OpenCV and Clipper2;
- optional angle classifier;
- multi-crop batching;
- game-window capture, scan orchestration, preview, cancellation, and workspace merge.

The Python OCR remains available side-by-side until native scan parity is verified.
