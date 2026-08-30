# Evidence — Modular Shell and Rotation MVP

## Snapshot

- Change：`modular-shell-rotation-mvp`
- Branch：`opsx/modular-shell-rotation-mvp`
- Base/initial HEAD：`1240c7384c3b842b5827c144cca814f320eb8245`
- Initial tree：clean

## Confirmed Decisions

The user explicitly confirmed the following scope decisions in Manual planning:

1. Completion line：a runnable Native MVP, not shell/core-only and not full controller/material tooling migration.
2. Game Tools：only relocate existing Convene link and map overlay; no new gacha export behavior.
3. Shell：left navigation with Dashboard landing page.
4. Profile source：Native versioned JSON plus explicit one-time wuwa-Hekili JSON import.
5. Input settings：defaults plus minimal keyboard/mouse binding UI.
6. Runtime boundary：accept input only while the verified game window is foreground; Alt-Tab pauses/hides.
7. Overlay visuals：generic action badges and key labels, with optional relative custom icons; no legacy asset copy.
8. Launch lifecycle：hide main window and restore the Rotation page when stopped.
9. Acceptance：real game smoke is required for closure.
10. Storage：follow the current Native data root (`<program>\data` with `WUWA_NATIVE_DATA_ROOT` override), without changing global persistence policy.
11. One change and branch：`modular-shell-rotation-mvp` on `opsx/modular-shell-rotation-mvp`.

## Repository Facts

- `src/Wuwa.App/MainWindow.xaml.cs` has 3609 lines and owns shell, achievement, OCR, map, Convene, hotkeys, theme and update behavior.
- Existing overlay precedents：`AchievementTrackerWindow` (Topmost/ShowActivated=false) and `MapOverlayWindow` (game client bounds tracking).
- Existing game-window/input boundary：`WindowsGameWindowCapture`; its OCR input-sending methods are forbidden dependencies for the new read-only Rotation path.
- Existing global hotkeys use `RegisterHotKey` in `MainWindow`; OCR occupies F12 combinations and map uses Alt+M/fallback.
- App manifest already requests administrator rights.
- Current source and README default mutable data to `AppPaths.DataDirectory == <program>\data`; `WUWA_NATIVE_DATA_ROOT` overrides the workspace store root.
- `.aiknowledge/domain.md` and parts of codemap still state `%LocalAppData%`, which conflicts with current source/README and is a knowledge-finalization finding rather than authority for this implementation.
- The source wuwa-Hekili repository has no clear license and its example profiles contain absolute machine paths; this change reimplements behavior and does not copy code/assets.

## Baseline Verification

Command:

```powershell
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
```

Result at initial snapshot：

- Passed：82
- Failed：0
- Skipped：4 environment-gated integration/smoke tests
- Total：86

Skipped surfaces：real OCR model/session smoke、live Wiki probe、visible-window capture smoke.

## Planning Findings

- **F-001 — MainWindow concentration** (`WARNING`, accepted): broad code-behind makes direct feature addition high regression risk. Disposition：Task 01 uses expand → migrate → contract and preserves Automation IDs.
- **F-002 — Input safety boundary** (`CRITICAL`, planned): the repository contains legitimate OCR `SendInput`/`mouse_event` paths. Disposition：Rotation gets a separate read-only adapter contract and static dependency verification.
- **F-003 — Data-root knowledge mismatch** (`WARNING`, accepted): long-term knowledge is stale against source/README. Disposition：follow current code; preserve a finalization finding for `.aiknowledge` after fresh verify.
- **F-004 — Real-game behavior cannot be proven by unit tests** (`CRITICAL`, accepted): focus, integrity level and overlay behavior require Windows game evidence. Disposition：Task 05 cannot close without real-game smoke.

## Current Verification Target

The highest public behavior seam is `RotationRunSession` and its observable snapshots. Production and scripted input/foreground adapters must drive the same contract. UI and real-game smoke verify integration without asserting private state or input call order.

## Implemented Snapshot

- `MainWindow` now hosts a left-navigation shell whose startup route is Dashboard. Achievement visuals are isolated in `AchievementWorkspaceView`; the existing `AchievementWorkspace`, OCR, tracker and exchange handlers remain the behavior boundary. Convene-link and map commands are routed through Game Tools.
- Native Rotation models, bindings validation, `RotationRunSession`, JSON profile/settings stores and the strict one-time Hekili importer are implemented under Core/Infrastructure.
- `RotationWorkbenchView` supports list/select/delete/import, isolated keyboard/mouse capture, validation and start gating.
- `WindowsRotationInputSource` ignores injected events, observes keyboard/mouse transitions, and calls `CallNextHookEx` from every low-level hook callback. The runtime coordinator owns game discovery, foreground gating, main-window hide/restore, fixed `Ctrl+Shift+F11` stop and idempotent cleanup.
- `RotationOverlayWindow` is Topmost, NoActivate, taskbar-hidden and click-through, displays current plus two preview steps, follows the game client and hides while foreground is lost.
- Publish includes `resources/rotation/action-badges.json` and rejects mutable Rotation state. Portable lifecycle checks preserve profile/settings bytes through launch, upgrade, uninstall simulation and reinstall.

## Fresh Automated and Elevated Verification

All commands below target the current implementation snapshot; no product-code change occurred after them.

1. Focused Rotation tests:
   - Command: `dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Rotation"`
   - Result: 23 passed, 0 failed, 0 skipped.
2. Full managed Release tests:
   - Command: `dotnet test WutheringWavesAchievement.sln -c Release --no-restore`
   - Result: 105 passed, 0 failed, 4 environment-gated tests skipped, 109 total.
3. Release build:
   - Command: `dotnet build WutheringWavesAchievement.sln -c Release --no-restore`
   - Result: 0 warnings, 0 errors.
4. Native publish:
   - Command: `scripts/publish-native.ps1 -Configuration Release -SkipTests`
   - Native OCR: 2/2 tests passed.
   - Package: 27 files, 177,748,342 bytes.
   - Archive: `publish/WutheringWavesAchievement-v2.1.0.zip`, SHA256 `b66c264312f269d562f2a9d77fb18d5b166b9fb72dbcedaacb7019f3df68a959`.
   - Audit: generic Rotation badge manifest present; 0 mutable Rotation files; 0 Python extensions/files.
5. Elevated final verification:
   - Evidence root: `artifacts/elevated-final/`.
   - `verify-ui`: PASS. Dashboard/Achievements/Rotation/Game Tools/Settings/Help routes and behavior-equivalent controls are reachable; Hekili fixture import and duplicate/missing binding gating pass.
   - `verify-tracker-ui`: PASS. Tracker controls are reachable and restore returns to Achievements.
   - `verify-rotation-runtime -RunWindowSmoke`: PASS. Static read-only boundary covers 10 dedicated production Rotation files plus MainWindow start/map handoff scopes. Visible surrogate lifecycle proves NoActivate/click-through styles, bounds/follow, foreground pause/resume, preview preservation and game-loss cleanup.
   - `verify-portable-lifecycle`: PASS. Initial manifest SHA256 `CC32A2E744E0269E9E514DF8DF66473A2D3B73597BBDCBB8BD08604A33E9AD38`.
   - Summary: `artifacts/elevated-final/summary.json` contains four PASS results; `status.txt` is `PASS`.
6. Elevated UI screenshots:
   - `1080x700-dark.png`: SHA256 `beb166b34f15b9d17b50ae884e4f40a39b6a1d67b25e1904624607f2ee7a8e44`.
   - `1080x700-light.png`: SHA256 `38b9af069b41a08888bc908a82312c5051a867e92cb76ba804df5d5940a4c328`.
   - `1440x900-dark.png`: SHA256 `3114ba81abc55d8ea5783ae700b7b94fa479a37153caeada172f6bb3435754a9`.
   - `1440x900-light.png`: SHA256 `5097374e879e439e86ae2810b59882770da9f736961b471e29ea6cda244041d5`.
7. Repository hygiene:
   - `git diff --check` reports no content errors; only existing LF→CRLF working-copy warnings.

## Real-game Physical-input Acceptance

The former F-004 blocking finding is closed. Authoritative local evidence is under `artifacts/real-game-smoke/`; `final-summary.md` maps each checklist item to observer timelines, screenshots and explicit user confirmations.

Environment:

- Real process: `Client-Win64-Shipping`, PID 51652, title `鸣潮`.
- Visible unminimized window: 1936×1119 outer bounds; 1920×1080 client area.
- Integrity: game `High`; final published Native app `High`.
- Final package executable with an isolated data root and a bounded START → Basic → Skill → Basic-loop profile.

Results:

1. Overlay focus safety: PASS. The game remained foreground after start; observer recorded `NoActivate=true`, `clickThrough=true`, overlay visible inside the real client.
2. No automatic action: PASS. The user observed at least five seconds at START without input and confirmed no automatic attack, movement, jump or skill; the timeline remained START.
3. Wrong input: PASS. Physical Space made the real character jump while the prompt remained START.
4. Matching input and pass-through: PASS. Physical F5 advanced START → Basic; physical mouse-left visibly executed a real-game normal attack and advanced Basic → Skill; physical E visibly executed the skill input and advanced Skill → Loop Basic.
5. Alt-Tab lifecycle: PASS. Run 1 recorded the overlay hiding while another window was foreground and reappearing with the identical Loop preview when the game returned.
6. Fixed stop: PASS. `Ctrl+Shift+F11` closed the overlay and restored the Native Rotation page; all three bounded runs ended with observer result `STOPPED_AND_RESTORED`.
7. Safety: PASS. No automatic game action was observed, the production static scan remains green, injected events are rejected and all low-level callbacks call `CallNextHookEx`.

## Residual Knowledge Finding

- **F-003 — Data-root knowledge mismatch (`WARNING`, pending Commit A finalization):** `.aiknowledge/domain.md` and parts of the codemap still state `%LocalAppData%`, while current source/README use `<program>\data` with `WUWA_NATIVE_DATA_ROOT` override. Correct it through the bounded knowledge finalization step after Commit A.

## Final Verification

- Snapshot：working tree on `opsx/modular-shell-rotation-mvp` over `1240c7384c3b842b5827c144cca814f320eb8245`, immediately before Commit A.
- Artifact gate：`spec.md`、`tasks.md`、`evidence.md` and `work/checkpoint.md` are non-empty; all five task checkboxes are complete; all modified/untracked paths are owned by this change.
- Fresh managed verification：105 passed, 0 failed, 4 explicitly environment-gated skipped; Release build 0 warnings / 0 errors. Focused Rotation verification remains 23/23 passed.
- Fresh static/package verification：PowerShell parsers pass; Rotation forbidden-API scan passes; `git diff --check` has no content error; published directory is restored to 27 files / 177,748,342 bytes with 26 manifest entries and zero hash mismatches; generic badges are present and mutable Rotation data, Python files and absolute user paths are absent.
- Elevated integration：UI、tracker、visible Rotation surrogate and portable lifecycle all PASS in `artifacts/elevated-final/summary.json`.
- Real-game integration：all seven physical-input/focus/safety/stop items PASS in `artifacts/real-game-smoke/final-summary.md`.
- Spec compliance：PASS. Shell routes, achievement/tool parity, profile/import/store/binding semantics, public session seam, foreground runtime, overlay lifecycle, safety boundary, docs and package behavior match `spec.md`.
- Release risk：PASS. No unresolved schema/data migration, mutable resource, input-injection, hook-cleanup, focus, package, portable-lifecycle, unknown-diff or manual-acceptance blocker remains.
- Knowledge handoff：verified. F-003 is bounded to post-Commit-A `.aiknowledge` finalization and does not alter the shipped implementation verdict.
