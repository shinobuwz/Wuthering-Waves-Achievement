# 01 — Native workspace tracer

**What to build:** Create the VS2022/.NET 8 solution and a runnable WPF vertical slice backed by `AchievementWorkspace`. Load the shipped library, expose query/filter behavior, execute status transitions, and render same-revision headline statistics. Include synthetic mutual-exclusion groups even though the shipped data currently has none.

**Blocked by:** None — can start immediately

**Suggested Files:** `WutheringWavesAchievement.sln`, `global.json`, `src/Wuwa.Core/`, `src/Wuwa.App/`, `tests/Wuwa.Tests/`

**Behavior Context:**

- `AchievementId` is immutable and `LegacyCode` remains visible/exportable.
- UI reads and mutates state only through `AchievementWorkspace`.
- Completing a group member occupies peers; reopening it resets the group; completing an occupied peer transfers completion.
- Query results and statistics must identify the same workspace revision.

**Acceptance:** The native app launches, loads 958 real rows, filters them, changes progress, updates metrics, and exercises grouped behavior through synthetic fixtures without touching legacy files.

**Verification:** Run focused workspace contract tests with `InMemoryAppDataStore`, `dotnet build -c Release`, and launch the WPF process against a temporary profile.
