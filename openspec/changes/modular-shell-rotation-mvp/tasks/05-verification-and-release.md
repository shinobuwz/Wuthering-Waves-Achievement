# 05 — Close verification and release evidence

**What to build:** Finish help/README/module copy, package inclusion rules for generic rotation resources, UI automation updates, portable lifecycle coverage and a bounded real-game smoke record. Update change evidence/checkpoint with fresh results and unresolved risk. Do not close this task on automated evidence alone.

**Blocked by:** Task 04.

**Suggested Files:** `README.md`, help/settings views, `src/Wuwa.App/Wuwa.App.csproj`, `scripts/verify-ui.ps1`, `scripts/verify-tracker-ui.ps1`, new rotation smoke script, `scripts/publish-native.ps1`, current change evidence/checkpoint.

**Behavior Context:**

- Documentation must state “只提示、不代替操作”, foreground-only behavior, stop shortcut, supported profile import and explicit non-support for controller/editor/icon capture.
- Published package may include generic immutable badges/sample schema, but not user profiles, bindings, imported Hekili files or absolute machine paths.
- Real-game smoke must use visible unminimized《鸣潮》in borderless/windowed mode at the same integrity level.

**Acceptance:** Automated suite/build/UI/tracker/rotation/portable checks pass on the final snapshot. Manual game evidence confirms overlay focus safety, pass-through input, correct/wrong action behavior, Alt-Tab pause/resume and stop restoration. If the environment is unavailable or any safety item is unproven, record a blocking finding and leave Task 05 unchecked.

**Verification:**

```powershell
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
dotnet build WutheringWavesAchievement.sln -c Release --no-restore
powershell -ExecutionPolicy Bypass -File scripts/verify-ui.ps1
powershell -ExecutionPolicy Bypass -File scripts/verify-tracker-ui.ps1
powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Configuration Release
powershell -ExecutionPolicy Bypass -File scripts/verify-portable-lifecycle.ps1
# Run the new rotation Windows smoke and the real-game checklist in spec.md.
```
