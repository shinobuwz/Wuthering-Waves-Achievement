# Native OCR Scan Tasks

- [x] **01 — Recognition runtime boundary**
  - Add CMake/MSVC x64 C++20 DLL, stable C ABI, structured errors, model/dictionary validation, dynamic-width recognition preprocessing, ONNX Runtime inference, CTC decode, C# safe handle, and real-model smoke.

- [ ] **02 — Detector and classifier**
  - Add OpenCV-based DB detector preprocessing/contours/rotated boxes, Clipper2 expansion, crop ordering, perspective transform, optional angle classifier, and golden Python differential fixtures.

- [ ] **03 — Native scan service**
  - Add game-window discovery/capture, current-page scan, cancellation, bounded worker scheduling, and typed scan result contracts independent of WPF.

- [ ] **04 — Global navigation and matching**
  - Port primary/secondary tab discovery, scrolling/navigation safeguards, achievement-name normalization/fuzzy matching, duplicate review, and full-scan progress reporting.

- [ ] **05 — Preview and transactional merge UI**
  - Add WPF scan surface, preview/selection/conflict UI, and accepted progress merge through `AchievementWorkspace`; failure/cancellation must preserve the active revision.

- [ ] **06 — Native OCR release verification**
  - Package DLLs and models, run C++/managed tests, captured-image differential checks, leak/cancellation stress, self-contained publish, and side-by-side launch verification before considering Python OCR removal.
