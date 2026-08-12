# Native Achievement Workspace Tasks

- [x] [01 — Native workspace tracer](tasks/01-native-workspace-tracer.md)
  - **Blocked by:** None
  - **Acceptance evidence:** WPF loads 958 shipped rows through `AchievementWorkspace`; query/status/statistics/group contracts pass; Release build and UI launch/capture pass.
  - **Verification:** `dotnet test`, `dotnet build -c Release`, `native/scripts/verify-ui.ps1`.

- [x] [02 — Transactional state and legacy import](tasks/02-transactional-state-and-legacy-import.md)
  - **Blocked by:** 01
  - **Acceptance evidence:** Versioned generations, commit markers, manifest recovery, pre/post-commit fault injection, legacy candidate validation, unknown-code rejection, read-only import and portable fixture migration pass.
  - **Verification:** Persistence/migration tests and `verify-portable-lifecycle.ps1 -LegacyConfig <copied fixture>`.

- [x] [03 — Anonymous Wiki reconciliation](tasks/03-anonymous-wiki-reconciliation.md)
  - **Blocked by:** 02
  - **Acceptance evidence:** HTTP/business/schema validation, normalized bootstrap identity retention, ambiguity quarantine, no-mutation failures, equivalent-content revision suppression and isolated live Wiki probe pass.
  - **Verification:** Wiki fixtures plus `native/scripts/verify-wiki-live.ps1`.
  - **Residual:** Wiki response caching remains non-authoritative/deferred; correctness does not depend on it.

- [x] [04 — JSON and Excel exchange](tasks/04-json-excel-exchange.md)
  - **Blocked by:** 02
  - **Acceptance evidence:** Chinese/English JSON aliases, progress merge, validation diagnostics, TSV and native OOXML `.xlsx` round-trip pass; UI exposes explicit JSON/Excel formats.
  - **Verification:** Exchange tests including invalid-candidate no-save and XLSX golden round-trip.

- [ ] [05 — Refined desktop shell and secure updates](tasks/05-refined-desktop-shell.md)
  - **Blocked by:** 01, 02
  - **Completed:** Both themes, secure/trusted update URL and cache fixtures, target-size control reachability, and four nonblank screenshots.
  - **Remaining user acceptance/verification:** Subjective visual/taste review plus end-to-end UI Automation for filter/status/stat/theme/error scenarios and hands-on keyboard/DPI/loading/empty/error workflow acceptance.
  - **Verification:** `native/scripts/verify-ui.ps1`; screenshots under ignored `native/artifacts/ui/`.

- [x] [06 — Side-by-side native release](tasks/06-side-by-side-native-release.md)
  - **Blocked by:** 03, 04, 05 automated portion
  - **Acceptance evidence:** Pinned SDK, C++ tests, self-contained `win-x64` publish, package manifest/contents checks, copied legacy migration, relaunch, portable upgrade/uninstall/reinstall simulation, and side-by-side docs pass.
  - **Verification:** `publish-native.ps1`, `verify-portable-lifecycle.ps1`, README and architecture diff review.
  - **Boundary:** This verifies the portable package lifecycle, not an MSI/MSIX installer lifecycle.
