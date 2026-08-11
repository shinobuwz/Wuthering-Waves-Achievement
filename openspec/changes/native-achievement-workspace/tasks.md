# Native Achievement Workspace Tasks

- [ ] [01 — Native workspace tracer](tasks/01-native-workspace-tracer.md)
  - **Blocked by:** None — can start immediately
  - **Suggested Files:** `native/WutheringWavesAchievement.sln`, `native/src/Wuwa.Core/`, `native/src/Wuwa.App/`, `native/tests/Wuwa.Tests/`
  - **Acceptance:** The WPF app loads the shipped 958-row library through `AchievementWorkspace`; users can query/filter, change status, and observe same-revision metrics. Synthetic group transitions work.
  - **Verification:** Focused workspace tests, `dotnet build`, WPF launch smoke.

- [ ] [02 — Transactional state and legacy import](tasks/02-transactional-state-and-legacy-import.md)
  - **Blocked by:** 01
  - **Suggested Files:** `native/src/Wuwa.Infrastructure/Persistence/`, `native/src/Wuwa.Core/Migration/`, `native/tests/Wuwa.Tests/Persistence/`
  - **Acceptance:** A selected legacy profile imports without changing legacy files; validated generations activate atomically and recover from injected interruption.
  - **Verification:** Adapter contract suite, migration fixtures, fault-injection recovery tests.

- [ ] [03 — Anonymous Wiki reconciliation](tasks/03-anonymous-wiki-reconciliation.md)
  - **Blocked by:** 02
  - **Suggested Files:** `native/src/Wuwa.Infrastructure/Wiki/`, `native/src/Wuwa.Core/Sync/`, `native/tests/Wuwa.Tests/Wiki/`
  - **Acceptance:** Valid anonymous responses reconcile by stable identity into a new generation; malformed, partial, ambiguous, or failed responses leave active data and progress unchanged.
  - **Verification:** Fixture tests plus isolated live API/cache probe.

- [ ] [04 — JSON and Excel exchange](tasks/04-json-excel-exchange.md)
  - **Blocked by:** 02
  - **Suggested Files:** `native/src/Wuwa.Infrastructure/Exchange/`, `native/src/Wuwa.App/Views/`, `native/tests/Wuwa.Tests/Exchange/`
  - **Acceptance:** Supported legacy JSON/Excel shapes import transactionally and export round-trips all contracted fields; invalid data produces reviewable errors without mutation.
  - **Verification:** Golden JSON/Excel round-trip and destructive-import rollback tests.

- [ ] [05 — Refined desktop shell and secure updates](tasks/05-refined-desktop-shell.md)
  - **Blocked by:** 01, 02
  - **Suggested Files:** `native/src/Wuwa.App/`, `native/src/Wuwa.Infrastructure/Updates/`, `native/tests/Wuwa.Tests/Ui/`
  - **Acceptance:** Management, statistics, and data views are complete in dark/light themes; update checks validate TLS; keyboard, DPI, loading, empty, and error states are usable.
  - **Verification:** UI Automation, dark/light screenshots at 1080x700 and 1440x900, update fixtures.

- [ ] [06 — Side-by-side native release](tasks/06-side-by-side-native-release.md)
  - **Blocked by:** 03, 04, 05
  - **Suggested Files:** `native/Directory.Build.props`, `native/scripts/`, `README.md`, `docx/项目架构分析.md`
  - **Acceptance:** A self-contained `win-x64` artifact launches, migrates copied legacy fixtures, preserves user data across reinstall/uninstall scenarios, and documents the side-by-side boundary without deleting Python.
  - **Verification:** Full tests, Release build/publish, packaged launch and migration smoke, scoped diff review.
