# 03 — Add the Rotation workbench and bindings UI

**What to build:** Add the Rotation module page to the shell. It must list Native profiles, import a wuwa-Hekili JSON file, select/delete profiles, show team and Opener/Loop summary, capture keyboard/mouse bindings, reject duplicates, display missing-action validation and expose a Start request only when the selected profile is runnable.

**Blocked by:** Tasks 01 and 02.

**Suggested Files:** new `RotationWorkbenchView.xaml/.cs` and supporting view-model/coordinator files in `src/Wuwa.App/`; shell navigation wiring; UI verification script additions.

**Behavior Context:**

- No visual step editor is included.
- Binding capture is read-only and local to the settings interaction; it must not install the long-running game Hook before Start.
- Actions with no safe default may remain unbound, but a profile requiring them cannot start.
- Hekili import displays warnings for stripped icons without converting them into failure when the sequence is otherwise valid.
- Delete requires confirmation and affects only the selected Native profile.

**Acceptance:** With an isolated data root, UI automation can navigate to Rotation, import a valid fixture, see it in the list, select it, edit bindings, observe duplicate/missing validation, and reach an enabled Start action only after requirements are satisfied.

**Verification:**

```powershell
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
dotnet build WutheringWavesAchievement.sln -c Release --no-restore
# Updated UI automation must cover Rotation controls with WUWA_NATIVE_DATA_ROOT isolation.
powershell -ExecutionPolicy Bypass -File scripts/verify-ui.ps1
```
