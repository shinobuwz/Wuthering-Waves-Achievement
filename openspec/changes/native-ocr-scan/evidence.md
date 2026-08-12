# Evidence

### Stable C ABI before scan automation | implementation
背景：The existing OCR is Python/NumPy/OpenCV/ONNX Runtime and the WPF application cannot safely consume Python object lifetimes or exceptions.
事件：Implemented ABI v1 with explicit handle ownership, UTF-8 result lifetime, status codes, two-pass error retrieval, and a managed `SafeHandle` wrapper.
结论：Detection and game automation can evolve behind the ABI without coupling WPF to C++ classes or Python.

### Recognition first | implementation
背景：Full PP-OCR requires detector DB postprocessing, Clipper2, perspective crops, optional classification, and recognition.
事件：The shipped recognition model has one dynamic-width `NCHW` input and `[batch,time,18385]` output; the 18,383-line dictionary plus blank and optional space exactly matches that class count.
结论：Implemented and verified recognition first, then extended the same ABI with the detector rather than replacing the proven boundary.

### Modern OpenCV plus vendored Clipper2 | implementation
背景：The only immediately available native OpenCV NuGet package was OpenCV 3.1/v140, which is too old to become the release toolchain.
事件：Pinned the official OpenCV 4.12.0 Windows SDK download by SHA-256 and vendored the small official Clipper2 C++ source with its Boost license.
结论：The detector builds on VS2022 with modern OpenCV while keeping the Clipper2 source auditable and avoiding an obsolete native package lock.

## Verification

- `powershell -ExecutionPolicy Bypass -File native/scripts/build-native-ocr.ps1 -Configuration Release -Clean`: passed; CMake/MSVC x64 build succeeded.
- CTest: 2/2 passed, including real shipped `det.onnx` + `rec.onnx` initialization/inference and DB postprocess smoke.
- Managed integration with `WUWA_NATIVE_OCR_ROOT` and `WUWA_NATIVE_OCR_MODEL_ROOT`: C# → C ABI → C++ → OpenCV/Clipper2/ONNX Runtime passed for line recognition and blank-page full OCR.
- `dotnet test native/WutheringWavesAchievement.sln -c Release --no-restore`: 18 passed, 0 failed.
- `dotnet build native/WutheringWavesAchievement.sln -c Release --no-restore`: 0 warnings, 0 errors.

Remaining risk: optional classifier, multi-crop batching, captured-image differential fixtures, game capture/navigation, preview/merge, and packaged native assets are not implemented in this checkpoint.
