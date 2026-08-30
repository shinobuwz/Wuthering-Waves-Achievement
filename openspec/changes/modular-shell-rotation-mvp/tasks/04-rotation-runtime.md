# 04 — Run the read-only foreground-bound overlay

**What to build:** Implement a Windows keyboard/mouse input source that only observes and forwards physical events, a game foreground/bounds monitor, a three-slot NoActivate/click-through WPF overlay, and the coordinator that hides/restores the main shell around a `RotationRunSession`. Register fixed `Ctrl+Shift+F11` stop handling without conflicting with existing OCR/map hotkeys.

**Blocked by:** Task 03.

**Suggested Files:** new input and foreground adapters in `src/Wuwa.Infrastructure/`; new overlay/runtime coordinator in `src/Wuwa.App/`; existing game-window discovery/bounds helpers; environment-gated Windows smoke tests/scripts.

**Behavior Context:**

- The Rotation input source must not call the OCR click/scroll/type methods in `WindowsGameWindowCapture`.
- Low-level hooks always call `CallNextHookEx` and never suppress game input.
- Start validates game presence and required bindings before hiding MainWindow.
- Foreground loss pauses without reset; return resumes the same snapshot.
- Invalid/closed game window stops and restores with an error.
- Overlay shows generic action badge, description/character and binding label; optional relative image may replace the badge.
- Cleanup is idempotent on stop, reselect, window close, application shutdown and initialization failure.

**Acceptance:** In a visible Windows test-window scenario, the overlay follows client bounds without activation, physical events pass through, matching events advance, wrong events do not, foreground loss hides/pauses, foreground return resumes, and stop/shutdown leave no Hook/timer/overlay alive. Static review proves the Rotation dependency graph contains no input-sending API.

**Verification:**

```powershell
dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Rotation"
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
dotnet build WutheringWavesAchievement.sln -c Release --no-restore
# New environment-gated Windows rotation smoke script/test.
# Forbidden API dependency search scoped to Rotation paths.
```
