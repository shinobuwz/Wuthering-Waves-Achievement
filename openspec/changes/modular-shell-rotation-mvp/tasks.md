# Tasks — Modular Shell and Rotation MVP

`tasks.md` is the only progress view. Task documents provide bounded implementation context.

- [x] **01 — Build the modular application shell without changing existing feature behavior** ([task doc](tasks/01-modular-shell.md))
  - **Blocked by:** None — can start immediately.
  - **Suggested Files:** `src/Wuwa.App/MainWindow.xaml`, `src/Wuwa.App/MainWindow.xaml.cs`, new Dashboard/Achievement/GameTools views, `scripts/verify-ui.ps1`, `scripts/verify-tracker-ui.ps1`.
  - **Acceptance:** Startup lands on Dashboard; navigation reaches Achievement、Rotation placeholder、Game Tools、Settings/Help; all existing achievement/OCR/tracker/map/convene/theme/update behaviors remain reachable and use their existing domain entry points.
  - **Verification:** `dotnet test WutheringWavesAchievement.sln -c Release --no-restore`; `dotnet build WutheringWavesAchievement.sln -c Release --no-restore`; updated `scripts/verify-ui.ps1`; `scripts/verify-tracker-ui.ps1`.

- [x] **02 — Deliver native rotation profiles, Hekili import, storage, bindings validation and the public run-session behavior** ([task doc](tasks/02-rotation-core.md))
  - **Blocked by:** None — can start immediately in Core/Infrastructure paths independent of Task 01.
  - **Suggested Files:** new rotation files in `src/Wuwa.Core/`, new JSON store/import adapter in `src/Wuwa.Infrastructure/`, new tests in `tests/Wuwa.Tests/`.
  - **Acceptance:** A valid Hekili JSON can be imported once into a versioned Native profile; invalid input is atomic; `RotationRunSession` exposes correct START/Opener/Loop/Heavy/Intro/pause/stop snapshots; duplicate or incomplete bindings are rejected.
  - **Verification:** focused rotation parser/import/store/session tests, then full Release tests.

- [x] **03 — Add the usable Rotation module page and minimal keyboard/mouse binding workflow** ([task doc](tasks/03-rotation-workbench.md))
  - **Blocked by:** Tasks 01 and 02.
  - **Suggested Files:** new Rotation workbench/profile list/binding controls in `src/Wuwa.App/`, shell navigation wiring, optional view models/coordinator.
  - **Acceptance:** User can import, list, select and delete Native profiles; configure non-duplicate keyboard/mouse bindings; see validation; and request start only when profile and required bindings are valid.
  - **Verification:** Core validation tests plus UI automation for Rotation navigation/import/binding controls using an isolated data root.

- [x] **04 — Run a foreground-bound, read-only keyboard/mouse session with the three-step overlay** ([task doc](tasks/04-rotation-runtime.md))
  - **Verified:** Public-session tests, forbidden-API scan, elevated visible-window lifecycle smoke and direct real-game physical keyboard/mouse pass-through all pass.
  - **Blocked by:** Task 03.
  - **Suggested Files:** new input/foreground adapters in `src/Wuwa.Infrastructure/`, new overlay/coordinator in `src/Wuwa.App/`, existing game-window bounds facilities, runtime tests/scripts.
  - **Acceptance:** Starting hides the main window and focuses the game; the click-through NoActivate overlay follows the game; only matching physical inputs advance; Alt-Tab pauses/hides; returning resumes; `Ctrl+Shift+F11`, game loss or application shutdown dispose listeners and restore safely; no input injection path is called.
  - **Verification:** public session tests with scripted input, Windows test-window smoke, static forbidden-API dependency check, full Release build/tests.

- [x] **05 — Close documentation, package, UI and real-game verification** ([task doc](tasks/05-verification-and-release.md))
  - **Blocked by:** Task 04.
  - **Suggested Files:** `README.md`, `src/Wuwa.App/Wuwa.App.csproj`, `scripts/verify-ui.ps1`, new rotation smoke script, publish verification, change evidence/checkpoint.
  - **Acceptance:** Help and README explain module boundaries and “only prompts”; publish contains required generic rotation resources but no mutable profile or legacy absolute path; all automated verification passes; a real-game smoke is recorded. If real-game smoke cannot run, the task remains open with a blocking finding.
  - **Verification:** Release test/build, UI/tracker/rotation smoke, portable lifecycle/publish checks, manual game checklist from `spec.md`.
