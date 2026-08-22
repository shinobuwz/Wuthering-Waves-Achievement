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

### Scan service stays UI-independent | implementation
背景：Window discovery, pixel capture, native inference, cancellation, preview, and progress mutation have different failure boundaries.
事件：Added `IGameWindowCapture`, `IOcrTextReader`, immutable BGR frames/results, and `SinglePageOcrScanService`; Win32 GDI and the C++ OCR handle are infrastructure adapters.
结论：WPF can cancel and preview one-page scans without owning HWND/GDI resources or mutating workspace progress during capture/inference.

### OCR only mutates progress after preview confirmation | implementation
背景：Raw OCR can be ambiguous or omit status text, and the legacy workflow intentionally prevents completed achievements from being downgraded.
事件：Added name normalization/edit-distance matching, ambiguity quarantine, date/in-progress parsing, nearest-line status association, `AchievementWorkspace.ApplyOcrPreviewAsync`, and a selectable WPF preview dialog.
结论：Scanning produces immutable review candidates; explicit confirmation applies selected statuses in one generation and preserves completed-status downgrade protection.

### Full-scan planning decision | planning
背景：Python already provides a working `scan_all_tabs()` flow, while Native currently scans only the selected secondary category. Replacing the known current-category command would make input/navigation regressions harder to isolate.
事件：Confirmed a second Native command for full scanning, preserving the existing current-category command. The plan ports Python's primary/secondary traversal, verification and bounded scrolling, per-category page scan, in-memory merge, progress reporting, cancellation, and final preview while reusing the existing same-integrity input adapter.
结论：Full scan stays within the existing OCR surface; it adds no top-level application tab, achievement-group field, or legacy-data mutation path. Native full scan remains incomplete until the task-04 coordinator contract tests and a manual same-integrity game smoke demonstrate visible tab/list changes.

## Verification

- `powershell -ExecutionPolicy Bypass -File scripts/build-native-ocr.ps1 -Configuration Release -Clean`: passed; CMake/MSVC x64 build succeeded.
- CTest: 2/2 passed, including real shipped `det.onnx` + `cls.onnx` + `rec.onnx` initialization/inference, DB postprocess, classifier, and CTC smoke.
- Managed integration with `WUWA_NATIVE_OCR_ROOT` and `WUWA_NATIVE_OCR_MODEL_ROOT`: C# → C ABI → C++ → OpenCV/Clipper2/ONNX Runtime passed for line recognition and blank-page full OCR.
- `dotnet test WutheringWavesAchievement.sln -c Release --no-restore`: scan/matching/apply contract tests and native det/cls/rec integration pass; 24 passed plus 1 opt-in capture smoke skipped.
- Real Win32 smoke launched the WPF app, found it by process name, captured its visible client area, and validated a non-black top-down BGR frame.
- `dotnet build WutheringWavesAchievement.sln -c Release --no-restore`: 0 warnings, 0 errors.
- `scripts/publish-native.ps1`: self-contained `win-x64` package succeeded and copied the C++ OCR DLL, ONNX Runtime, OpenCV, det/rec models, dictionary, and third-party notice into `ocr/`.
- Managed OCR integration smoke passed against the packaged `ocr/` directory.
- Packaged WPF executable stayed alive for 5 seconds with temporary `WUWA_NATIVE_DATA_ROOT`, created `current.json`, and found its packaged OCR DLL.

Remaining risk: multi-crop batching, captured game-image differential fixtures, DirectX exclusive-fullscreen capture, and global tab navigation are not implemented in this checkpoint.
