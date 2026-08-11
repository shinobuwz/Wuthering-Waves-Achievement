**Verdict**

**CHANGES REQUIRED.** The four-change direction is coherent, but Change 1 is not implementation-ready. The unresolved identity, transaction, migration ownership, and toolchain decisions can cause progress loss or make the stated VS2022 target impossible.

**Critical Findings**

1. **CRITICAL: `net10.0-windows` conflicts with the VS2022 requirement.**

   The inspected environment has .NET SDK `10.0.105` using MSBuild `18.0.11`; no VS2022 installation was discovered. .NET 10’s supported Visual Studio baseline is the VS2026/MSBuild 18 generation, not VS2022/MSBuild 17.

   **Disposition:** Choose one explicit baseline before implementation:
   - Preserve VS2022 and target a VS2022-supported framework, or
   - Preserve .NET 10 and change the requirement to VS2026/CLI tooling.

   Add `global.json`, exact minimum IDE/SDK versions, Windows SDK version, and CI enforcement.

2. **CRITICAL: Wiki synchronization has no safe identity/remapping contract.**

   Progress is keyed by mutable `编号` in [config.py](E:/gitlab/Wuthering-Waves-Achievement/core/config.py:428). Current synchronization identifies remote records using `(名称, cleaned 描述)` in [main_window.py](E:/gitlab/Wuthering-Waves-Achievement/core/main_window.py:300), while re-encoding can regenerate every `编号` and `绝对编号` in [manage_tab.py](E:/gitlab/Wuthering-Waves-Achievement/core/manage_tab.py:1538). The 958-row data also contains three achievements named `存在的视界线`, so name alone is not unique.

   “Adds/updates base data without deleting progress” does not define how description/category changes, renames, duplicate names, remote removals, or ID changes retain attribution.

   **Disposition:** Specify a canonical identity and immutable native key; define remote matching, ambiguous-match quarantine, field update precedence, tombstones/removals, and old-to-new progress mapping. Tests must cover rename, changed description, duplicate name, reordered category, remote partial response, and removal.

3. **CRITICAL: One-time migration and side-by-side operation can fork user state.**

   Legacy profile resolution depends on `current_user`, `users`, and per-user `uid` in [config.py](E:/gitlab/Wuthering-Waves-Achievement/core/config.py:204). The legacy app falls back to the first user or creates `本地档案` in [main_window.py](E:/gitlab/Wuthering-Waves-Achievement/core/main_window.py:29). The draft does not define migration behavior for invalid current users, multiple users, missing UID files, or later edits made in the still-available Python application.

   **Disposition:** Define deterministic selection and a visible confirmation containing nickname, UID, source path, and progress count. Define whether legacy becomes logically read-only, whether changed legacy files can be re-imported, and how final cutover resolves divergent native/legacy states.

4. **CRITICAL: “Atomic persistence” lacks a multi-file commit model.**

   The domain spans base data, category data, profile progress, settings, cache metadata, and migration marker. Current code writes progress directly in [config.py](E:/gitlab/Wuthering-Waves-Achievement/core/config.py:419), and synchronization persists base data before separately re-encoding progress in [main_window.py](E:/gitlab/Wuthering-Waves-Achievement/core/main_window.py:434). Per-file temp-and-replace is not an atomic transaction across that set.

   **Disposition:** Make `JsonAppDataStore` commit one versioned aggregate snapshot, or use generation directories plus an atomically replaced manifest pointer. Validate the full generation before activation. Add fault-injection recovery tests at every write/rename boundary.

**Warnings**

- **WARNING: The 958-row fixture cannot prove group behavior.** The shipped file has 958 rows but zero `成就组ID` records. Mutual exclusion marks peers `已占用` in [achievement_table.py](E:/gitlab/Wuthering-Waves-Achievement/core/achievement_table.py:703), while grouped statistics have separate logic in [manage_tab.py](E:/gitlab/Wuthering-Waves-Achievement/core/manage_tab.py:1247). Add synthetic two- and three-member groups covering complete, reopen, unavailable, occupied, filtering, import, and migration.

- **WARNING: “Supported legacy columns and formats” is underspecified.** Excel uses twelve columns including group metadata in [manage_tab.py](E:/gitlab/Wuthering-Waves-Achievement/core/manage_tab.py:1047), while full JSON export omits group fields in [manage_tab.py](E:/gitlab/Wuthering-Waves-Achievement/core/manage_tab.py:1687). Publish an accepted-shape matrix and golden round-trip fixtures rather than preserving accidental legacy bugs.

- **WARNING: Network validation needs explicit acceptance rules.** The Wiki request parses JSON without an HTTP status check in [crawl_tab.py](E:/gitlab/Wuthering-Waves-Achievement/core/crawl_tab.py:106), and the existing update checker disables TLS verification in [update.py](E:/gitlab/Wuthering-Waves-Achievement/core/update.py:75). Require success status, schema validation, plausible row thresholds, cache integrity, normal TLS validation, and no writes on malformed responses.

**Split And Seam Assessment**

The four changes are directionally coherent: workspace first, OCR second, overlay third, cutover last. The overlay does not inherently depend on OCR, so that dependency should remain organizational rather than architectural. Change 1 is broad but bounded if its critical contracts are resolved before coding.

`AchievementWorkspace` currently **fails the completion test**: its stated surface covers load/query/status/statistics, but Change 1 also routes migration, sync, import/export, and transactional replacement through the workspace. Those operations otherwise force UI or infrastructure bypasses.

It **does not yet pass the deletion/replacement test** because no native code exists and constructor injection/contract suites are only proposed. It does name **two adapters**, `JsonAppDataStore` and `InMemoryAppDataStore`, which is sufficient in design only after the same workspace contract suite runs against both and the WPF layer has no direct file access.

**Verification Blockers**

Resolve the toolchain baseline, canonical identity, profile-selection policy, side-by-side ownership, snapshot transaction format, import compatibility matrix, and validated Wiki response policy. Add golden legacy fixtures, grouped fixtures, crash recovery, malformed/partial network responses, adapter parity, packaged migration, and install/upgrade/uninstall tests. UI screenshots alone do not cover these risks.

No files were edited or staged by this review. Existing `openspec/` deletions were left untouched.