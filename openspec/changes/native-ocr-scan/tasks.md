# Native OCR Scan Tasks

- [x] **01 — Recognition runtime boundary**
  - Add CMake/MSVC x64 C++20 DLL, stable C ABI, structured errors, model/dictionary validation, dynamic-width recognition preprocessing, ONNX Runtime inference, CTC decode, C# safe handle, and real-model smoke.

- [ ] **02 — Detector and classifier**
  - [x] Add OpenCV-based DB detector preprocessing/contours/rotated boxes, Clipper2 expansion, crop ordering, perspective transform, optional 0°/180° classifier, full-page C ABI, and real det/cls/rec model smoke.
  - [ ] Add multi-crop batching and golden Native differential fixtures.

- [x] **03 — Native scan service**
  - Added process-name game-window discovery, client-area GDI capture to top-down BGR, current-page scan orchestration, cancellation/failure isolation, serialized native inference, and typed scan contracts independent of WPF.

- [ ] **04 — Native full-scan navigation and matching** ([task doc](tasks/04-global-navigation-and-matching.md))
  - [x] Port achievement-name normalization, Levenshtein matching threshold, duplicate ambiguity quarantine, date/in-progress status parsing, and same-row status association.
  - [ ] Add the separate full-scan command while preserving current-category scanning.
  - [ ] Complete Native primary/secondary tab discovery, verified clicks, bounded tab scrolling, per-category page scanning, progress reporting, cancellation, and no-write-before-preview merge.
  - **Blocked by:** 03 is complete; no implementation blocker. **Environment prerequisite:** game and tool run at the same integrity level.
  - **Acceptance:** A manual run visibly changes primary tabs, secondary tabs, and achievement list pages, then produces one multi-category preview without mutating workspace state before confirmation.
  - **Verification:** Coordinator contract tests with fake navigation, native ABI tab-OCR smoke, and a Windows same-integrity manual full-scan smoke.

- [x] **05 — Preview and transactional merge UI**
  - Added an OCR single-page command with cancel/minimize/restore lifecycle, immutable candidates, selectable preview, unmatched/unknown status feedback, and confirmed one-revision merge through `AchievementWorkspace`, including completed-status downgrade prevention.

- [ ] **06 — Native OCR release verification**
  - Package DLLs and models, run C++/managed tests, captured-image differential checks, leak/cancellation stress, self-contained publish, and Native-only launch verification.
