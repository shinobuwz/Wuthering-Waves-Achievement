# Native OCR Scan Tasks

- [x] **01 — Recognition runtime boundary**
  - Add CMake/MSVC x64 C++20 DLL, stable C ABI, structured errors, model/dictionary validation, dynamic-width recognition preprocessing, ONNX Runtime inference, CTC decode, C# safe handle, and real-model smoke.

- [ ] **02 — Detector and classifier**
  - [x] Add OpenCV-based DB detector preprocessing/contours/rotated boxes, Clipper2 expansion, crop ordering, perspective transform, full-page C ABI, and real-model smoke.
  - [ ] Add optional angle classifier, multi-crop batching, and golden Python differential fixtures.

- [x] **03 — Native scan service**
  - Added process-name game-window discovery, client-area GDI capture to top-down BGR, current-page scan orchestration, cancellation/failure isolation, serialized native inference, and typed scan contracts independent of WPF.

- [ ] **04 — Global navigation and matching**
  - [x] Port achievement-name normalization, Levenshtein matching threshold, duplicate ambiguity quarantine, date/in-progress status parsing, and same-row status association.
  - [ ] Port primary/secondary tab discovery, scrolling/navigation safeguards, and full-scan progress reporting.

- [x] **05 — Preview and transactional merge UI**
  - Added an OCR single-page command with cancel/minimize/restore lifecycle, immutable candidates, selectable preview, unmatched/unknown status feedback, and confirmed one-revision merge through `AchievementWorkspace`, including completed-status downgrade prevention.

- [ ] **06 — Native OCR release verification**
  - Package DLLs and models, run C++/managed tests, captured-image differential checks, leak/cancellation stress, self-contained publish, and side-by-side launch verification before considering Python OCR removal.
