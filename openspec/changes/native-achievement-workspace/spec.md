# Native Achievement Workspace

## Intent

Deliver a side-by-side Windows-native achievement manager that can replace the non-OCR daily workflow without risking the existing Python application's data. The application uses Visual Studio 2022, C#/.NET 8, and WPF. It preserves the current achievement library, progress semantics, statistics, anonymous Wiki synchronization, JSON/Excel exchange, theme, and update-check behavior while establishing stable identity and transactional persistence for later native OCR and tracker-overlay changes.

## Change Roadmap

This migration is intentionally split into four serial changes:

1. **native-achievement-workspace** (this change): native management, statistics, Wiki sync, exchange, theme, update check, migration, and side-by-side packaging.
2. **native-ocr-scan**: project-specific C++ PP-OCRv5 runtime, Windows game capture/automation, single-page/global scans, cancellation, preview, and safe progress merge.
3. **tracked-achievement-overlay**: persistent tracked achievements and an independent topmost overlay with edit/locked click-through modes.
4. **native-cutover-release**: final parity verification, release packaging, Python/Qt removal, and authoritative cutover.

The overlay may consume the workspace without depending on OCR; the ordering is organizational so each release has one active implementation frontier.

## Scope

### In Scope

- A `net8.0-windows` WPF solution that builds with Visual Studio 2022 and `dotnet` CLI.
- One local native profile, with explicit import of one selected legacy profile.
- Existing Chinese achievement fields and status semantics.
- Search, filtering, sorting, status transitions, group mutual exclusion, and live statistics.
- Anonymous Kuro Wiki synchronization with validation, caching, reconciliation, and failure isolation.
- JSON and Excel import/export for explicitly supported legacy shapes.
- Dark/light theme and secure GitHub release checking.
- Side-by-side self-contained `win-x64` publish while the Python application remains available.

### Non-Goals

- Native OCR, game-window capture, mouse automation, or scan UI.
- Tracked-achievement overlay or global shortcuts.
- Multi-user administration, avatars, character artwork, category editing, or achievement-group editing.
- Automatic bidirectional synchronization with the legacy Python application.
- Deleting or modifying the Python implementation, legacy data, or the user's existing `openspec/` deletions.
- Preserving accidental import/export bugs that are not part of the accepted compatibility matrix.

## Canonical Terms

- **AchievementId**: immutable native identifier. Existing records derive it deterministically from the legacy `编号`; newly discovered records derive it from an accepted Wiki source reference. It never changes when display fields or categories change.
- **LegacyCode**: the existing `编号` value retained for compatibility and export. It is not the native identity.
- **WikiSourceRef**: `<entry-id>/<table-data-uid>/<row-data-index>`. It is preferred for remote reconciliation but is not treated as immutable when the Wiki rebuilds a table.
- **Progress Status**: exactly one of `未完成`, `已完成`, `暂不可获取`, or `已占用`.
- **Tracked Achievement** and **Tracker Overlay** are reserved for change 3 and have no behavior in this change.

## Observable Behavior

### Startup And Profile Import

1. The native application launches directly into the achievement workspace and loads the shipped `base_achievements.json` and `category_config.json`.
2. If no native state exists, it discovers legacy `resources/config.json` and its referenced `resources/user_progress_{uid}.json` files without changing them.
3. When one valid legacy profile is unambiguous, the migration surface shows nickname, UID, source path, and progress count before import. When several candidates exist or `current_user` is invalid, the user selects one candidate.
4. Import creates a validated native snapshot. The migration is recorded only after the new snapshot is activated successfully.
5. The native application never watches or writes legacy files. A later legacy re-import is an explicit replace operation that first preserves the current native generation and requires confirmation.
6. If no valid legacy profile exists, the application creates an empty local profile over the shipped library.

### Achievement Management

1. The workspace loads all 958 shipped achievements without UI blocking and displays them in a virtualized table.
2. Users can search name and description; filter version, first category, second category, hidden state, obtainable state, and completion; and select default or incomplete-first ordering.
3. Status changes persist immediately as a new workspace revision and update visible rows and statistics.
4. Completing one member of an achievement group sets its mutually exclusive peers to `已占用`. Reopening the completed member returns all group members to `未完成`. Completing an occupied member first reopens the group and then completes the selected member.
5. Grouped achievements count once in totals and completion statistics. Synthetic group fixtures are authoritative because the shipped 958-row file currently contains no group metadata.
6. Unknown status values are rejected during interactive changes and quarantined during import rather than silently converted.

### Statistics

1. Metrics expose total, completed, incomplete, unavailable, hidden, grouped-choice count, and completion rate.
2. Category, subcategory, and version distributions use the current query filters and the same group-counting rules as headline metrics.
3. Statistics update in the same completed workspace revision as a status/import/sync change; the UI does not calculate a separate divergent copy.

### Wiki Synchronization

1. The client sends an anonymous POST to the existing Kuro Wiki endpoint with `wiki_type=9`; no credentials are stored or transmitted.
2. A response is accepted only when HTTP status is successful, business response indicates success, the expected content/table schema is present, and the parsed row count is plausible relative to the last valid library. A malformed, partial, or implausibly small response activates no state.
3. Parsed rows retain `WikiSourceRef`. Reconciliation uses this precedence:
   - existing exact `WikiSourceRef`;
   - unique normalized signature `(名称, 描述, 第一分类, 第二分类)`;
   - unique normalized `(名称, 描述)` fallback for legacy bootstrap only;
   - otherwise quarantine as ambiguous and leave active data unchanged for that row.
4. A matched remote row retains its `AchievementId` and `LegacyCode` while accepted descriptive fields update.
5. New unambiguous rows receive a new stable `AchievementId` and non-conflicting `LegacyCode`. Remote removals become tombstones first; they do not delete progress in this change.
6. Network, parse, validation, or reconciliation failure leaves the current generation active and reports an actionable error. Cache request fields (`traceId`, browse count) do not influence content equality.
7. A live Wiki probe always runs against temporary native state and never user data.

### Persistence And Recovery

1. Mutable native state lives under `%LocalAppData%/WutheringWavesAchievement` and immutable shipped resources remain beside the application.
2. `JsonAppDataStore` writes a complete versioned generation containing library, categories, profile progress, settings, identity/source mappings, tombstones, and metadata.
3. A generation is fully written and validated before an atomically replaced manifest pointer makes it current. Cache files are non-authoritative and may be updated separately.
4. Interruption at any write, flush, validation, or pointer-replacement boundary leaves either the prior valid generation or the complete new generation loadable.
5. At least the three newest valid generations are retained for recovery. The application never deletes the last valid generation.

### Import And Export

Supported inputs are explicit:

| Format | Accepted shape |
|---|---|
| Progress JSON | Object keyed by legacy code with `获取状态` |
| Full JSON | Array of achievement objects using Chinese fields or documented English aliases |
| Excel | Optional information row, then the 12 legacy columns: `绝对编号`, `版本`, `第一分类`, `第二分类`, `编号`, `名称`, `描述`, `奖励`, `是否隐藏`, `获取状态`, `成就组ID`, `互斥成就` |

1. Import parses and validates into a candidate generation before activation.
2. Destructive replacement requires confirmation and retains the current generation as rollback history.
3. Missing required fields, unknown categories, duplicate identity, invalid status, or ambiguous group references produce a reviewable error and no active-state mutation.
4. JSON and Excel exports round-trip names, Unicode, statuses, hidden flags, group IDs, and mutual-exclusion codes through golden fixtures.

### Theme, Updates, And Window Behavior

1. The main workspace uses a restrained dark charcoal visual system with teal focus/accent and a complete light theme; preference persists in native state.
2. The table, filters, commands, dialogs, loading/empty/error states, keyboard focus, high DPI, and 1080x700 minimum viewport remain usable without overlap.
3. GitHub update checking uses normal certificate validation, a bounded timeout, cached results, and opens the release page only on explicit user action.
4. The app does not open a landing page, settings dashboard, authentication window, or promotion surface on startup.

## Technical Decisions

- **Toolchain:** Visual Studio 2022, .NET SDK pinned by `global.json`, `net8.0-windows`, x64. Development builds are framework-dependent; release verification includes self-contained `win-x64` publish.
- **Projects:** `Wuwa.App`, `Wuwa.Core`, `Wuwa.Infrastructure`, and `Wuwa.Tests` in one solution.
- **Public behavior seam:** `AchievementWorkspace`. Its public operations open state, execute typed commands (status, migration, import, sync), query views, export data, and return a completed revision containing the observable workspace snapshot or structured failure. The WPF layer never reads JSON or calls HTTP directly.
- **Adapters:** `JsonAppDataStore` is the production state adapter and `InMemoryAppDataStore` is the independent test adapter. Both run the same workspace contract suite. Wiki HTTP and fixture clients are system-boundary adapters used behind workspace commands.
- **Identity:** deterministic UUIDv5-style IDs for legacy bootstrap and accepted source references; `LegacyCode` remains a compatibility attribute.
- **Persistence:** generation directory plus atomic current-manifest pointer, not independent in-place file updates.
- **UI:** WPF MVVM with built-in virtualization. Add dependencies only where they remove real implementation risk; core behavior remains independent of WPF.

## Testing Decisions

### Highest Public Seam

Tests exercise `AchievementWorkspace` through public commands, queries, completed revisions, and exported artifacts. They do not inspect private view-model fields, internal call order, or generation file layout.

The seam passes the intended completion test when every command returns either:

- a new immutable revision with queryable data/statistics, or
- a structured failure with the previous revision still active.

Deletion of `AchievementWorkspace` removes the application's management, migration, sync, import/export, and statistics behavior, so it is not a forwarding wrapper. The same behavior contract runs with both store adapters; production-only crash recovery has additional black-box process/file-system tests.

### Required Verification

- `dotnet test` for workspace behavior, group transitions, identity reconciliation, adapter parity, migration, import/export, and failure isolation.
- Differential fixtures generated from the current Python behavior for filtering/statistics and accepted legacy files.
- Fault-injection tests at each generation activation boundary.
- Isolated live anonymous Wiki probe plus malformed/partial fixture responses.
- `dotnet build -c Release` and self-contained `win-x64` publish.
- WPF process launch plus UI Automation for load/filter/status/stat refresh.
- Screenshots at 1080x700 and 1440x900 in dark/light themes, inspected for clipping, overlap, focus, and nonblank content.
- Packaged migration smoke test using copied legacy fixtures; installation and uninstall must not remove user state.

## Risks And Mitigations

- **Schema/data migration:** generation activation, deterministic identity, explicit profile selection, rollback generations, and golden fixtures.
- **Remote identity drift:** source reference plus conservative signature fallback and ambiguity quarantine.
- **Side-by-side divergence:** one-way explicit legacy import; no watcher or hidden synchronization.
- **Broad rewrite:** four serial changes with a runnable side-by-side app at the end of each change.
- **Group behavior absent from real data:** independent synthetic fixtures drive the contract.
- **Network corruption:** schema/plausibility checks and no-write failure behavior.
- **UI regressions:** behavior seam tests plus UI Automation and real screenshots.

## Auto-Mode Decisions

- Chose four changes instead of one large cutover so each stage is runnable and reversible.
- Chose WPF/.NET 8 instead of .NET 10 to preserve the explicit Visual Studio 2022 requirement.
- Chose one-way explicit legacy import instead of bidirectional coexistence to prevent silent state forks.
- Chose a native immutable ID instead of `编号` or name as identity because Wiki updates and category re-encoding can change legacy codes and fields.
- Chose tombstones instead of immediate remote deletion to preserve progress and make reconciliation recoverable.
- Chose a versioned aggregate snapshot instead of per-file atomic writes because library and progress must advance together.
