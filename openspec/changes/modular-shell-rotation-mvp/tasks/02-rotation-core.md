# 02 — Deliver rotation core, import and persistence

**What to build:** Implement Native rotation models, a versioned profile document, strict wuwa-Hekili one-time importer, atomic JSON profile/settings store, binding validation and the public `RotationRunSession` state machine. Keep Core free of WPF and Win32. Use injectable monotonic time for Heavy behavior. Do not persist absolute icon paths or modify imported files.

**Blocked by:** None — can start immediately in Core/Infrastructure paths independent of Task 01.

**Suggested Files:** new rotation files in `src/Wuwa.Core/`; new rotation JSON/import/store files in `src/Wuwa.Infrastructure/`; new `Rotation*Tests.cs` in `tests/Wuwa.Tests/`.

**Behavior Context:**

- Session states must cover Idle/AwaitingStart/Running/Paused/Finished/Stopped or equivalent public meanings.
- `START` is logical runtime state, not a user-authored profile step.
- Ordinary actions require matching down + matching up.
- Heavy uses the Basic physical action, matches one hold identity, and advances only after threshold on release.
- Intro includes target slot identity.
- Preview contains current + next two across Opener/Loop boundaries.
- Import failure is atomic; icon path stripping is warning-level when action semantics remain valid.
- Native profiles and bindings live under an explicit/current Native data root, separate from achievement generation.

**Acceptance:** Public tests can construct/import/store a profile, run it through START/Opener/Loop/Finished, pause/resume it, and prove wrong releases/repeated downs/short Heavy do not advance. Invalid imported files do not create or replace Native profiles. Binding validation reports duplicates and missing required actions.

**Verification:**

```powershell
dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Rotation"
dotnet test WutheringWavesAchievement.sln -c Release --no-restore
```
