# 01 — Build the modular application shell

**What to build:** Replace the single workspace-root composition with a left-navigation shell and Dashboard landing page. Extract the existing achievement surface and existing Game Tools entries without changing their public behavior. Keep `AchievementWorkspace` as the achievement command/query boundary and preserve OCR/tracker/map/Convene/theme/update/help reachability. Use expand → migrate → contract: introduce the shell/views, route existing behavior through them, verify parity, then remove obsolete root layout/code only after the new route is proven.

**Blocked by:** None — can start immediately.

**Suggested Files:** `src/Wuwa.App/MainWindow.xaml`, `src/Wuwa.App/MainWindow.xaml.cs`, new Dashboard/Achievement/GameTools/Settings views under `src/Wuwa.App/`, `scripts/verify-ui.ps1`, `scripts/verify-tracker-ui.ps1`.

**Behavior Context:**

- Startup route is Dashboard.
- Sidebar routes to Achievement、Rotation placeholder、Game Tools、Settings and Help.
- Dashboard reads existing snapshot/statistics; it does not calculate achievement semantics itself.
- Existing action handlers may temporarily remain coordinated by MainWindow, but final MainWindow ownership should be navigation/lifecycle/global coordination rather than rendering all feature controls.
- OCR stays inside Achievement; Map and Convene move to Game Tools.
- Existing Automation IDs remain attached to behavior-equivalent controls where possible.

**Acceptance:** Existing achievement and tool behaviors remain accessible after navigation; tracker restoration returns to the correct shell page; OCR navigation and cancellation still work; map hotkey and overlay lifecycle still work; visual verification can reach all required controls through the new shell.

**Verification:**

```powershell
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
dotnet build WutheringWavesAchievement.sln -c Release --no-restore
powershell -ExecutionPolicy Bypass -File scripts/verify-ui.ps1
powershell -ExecutionPolicy Bypass -File scripts/verify-tracker-ui.ps1
```
