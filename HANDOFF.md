# Handoff — Native Wuthering Waves Achievement Rewrite

## Next-session focus

Continue **Task 01 (native workspace tracer)** from the partial implementation, then verify and report before starting later changes. The immediate user request for this session was to create this handoff at the repository root and commit it together with the currently scoped work.

## Repository state

- Base commit before this work: `a272193 refactor: 移除 Wiki 鉴权并修复缓存校验`.
- Existing user-owned deletions under `openspec/` are intentional and must remain deleted. Do not restore, stage, or rewrite them.
- The new planning/implementation material is currently uncommitted:
  - `openspec/changes/native-achievement-workspace/`
  - `native/`
  - this `HANDOFF.md`
- `.pi-subagents/` is orchestration/runtime output and must not be committed.
- No native implementation commit has been made yet.

## Authoritative planning artifacts

Read these before changing behavior; do not duplicate their contents in another plan:

- `openspec/changes/native-achievement-workspace/spec.md`
- `openspec/changes/native-achievement-workspace/tasks.md`
- `openspec/changes/native-achievement-workspace/evidence.md`
- `openspec/changes/native-achievement-workspace/tasks/01-native-workspace-tracer.md`
- Later task packets in the same directory define persistence/migration, Wiki reconciliation, exchange, shell polish, and side-by-side release boundaries.

The independent plan review concluded `CHANGES REQUIRED`; its findings were dispositioned in the active spec/evidence. The important resolved decisions are: Visual Studio 2022 with `.NET 8`/`net8.0-windows`, immutable native `AchievementId` plus retained `LegacyCode`, explicit one-way legacy import, and versioned aggregate generations with an atomic manifest for later persistence work.

## Current implementation checkpoint

The Task 01 worker created a solution and began public contract tests before the worker process failed twice due to upstream transport errors (`502`, then `stream_read_error`). The red phase was observed: the focused tests initially failed to compile because the workspace contracts were absent. Partial files now exist under `native/`:

- `native/WutheringWavesAchievement.sln`
- `native/global.json`
- `native/src/Wuwa.Core/Contracts.cs`
- `native/src/Wuwa.Core/Models.cs`
- `native/src/Wuwa.Core/AchievementWorkspace.cs`
- `native/src/Wuwa.Core/Wuwa.Core.csproj`
- `native/src/Wuwa.Infrastructure/Wuwa.Infrastructure.csproj` (currently only a project shell)
- `native/src/Wuwa.App/` (currently a mostly empty template WPF shell)
- `native/tests/Wuwa.Tests/` including `AchievementIdentityTests.cs`, `ShippedJsonAchievementLibrarySourceTests.cs`, and a large `UnitTest1.cs` containing the workspace contract tests.

Obsolete template files such as `Class1.cs` and the default test name may be removed or renamed only within `native/**` once the implementation is complete.

## Required continuation

1. Inspect the existing native files and compile state; do not start a second writer or overwrite the partial work blindly.
2. Finish Task 01 only:
   - WPF-free `AchievementWorkspace` public behavior seam.
   - `InMemoryAppDataStore` and a read-only shipped JSON/category library source.
   - Immutable deterministic `AchievementId` from legacy code.
   - Four statuses: `Incomplete`, `Completed`, `Unavailable`, `Occupied`.
   - Search/filter/sort, same-revision statistics, and 2-/3-member synthetic group transitions.
   - WPF composition that loads the shipped 958-row library through the workspace, with a dense dark charcoal/teal tracer UI and virtualized table.
3. Do not implement Task 02 transactional generations, migration, Wiki sync, JSON/Excel exchange, Task 05 polish, OCR, overlay, or cutover yet.
4. Keep all implementation edits under `native/**`; do not alter Python sources, resources, README, existing deleted OpenSpec paths, or other user-owned files.
5. Run and record:
   - focused red/green tests as applicable;
   - `dotnet test`;
   - `dotnet build -c Release`;
   - validation that the shipped source loads exactly 958 rows;
   - bounded WPF launch smoke in a Windows-capable environment.
6. Update only the active change’s `evidence.md` final verification section after implementation verification. Keep evidence concise and direction-changing.
7. Before committing, use path-limited staging. Explicitly exclude `.pi-subagents/`, build outputs, and all unrelated pre-existing deletions unless the user explicitly changes that instruction.

## Known implementation risks

- Validate the actual JSON field types: shipped records use Chinese keys, `绝对编号`/`编号` are string-like, and `是否隐藏` is currently an empty string for visible rows.
- `category_config.json` stores second-category order values as strings; normalize them safely.
- The shipped 958-row data has no `成就组ID`; synthetic fixtures are required to test mutual exclusion and grouped statistics.
- The active core draft may contain correctness issues; test public behavior rather than trusting it. Pay particular attention to filtered statistics, group status transitions, persistence failure behavior, and cancellation handling.
- The active spec intentionally says the native UI must not read files directly. Resource loading belongs behind the infrastructure adapter and workspace seam.

## Commit guidance

The user asked for the handoff and current work to be committed together. After the Task 01 implementation is genuinely complete and validated, create one appropriately scoped commit containing:

- `HANDOFF.md`;
- `native/**` Task 01 files;
- `openspec/changes/native-achievement-workspace/**` planning artifacts, if they remain part of this change.

Do not include `.pi-subagents/`, `bin/`, `obj/`, generated screenshots, secrets, or unrelated deleted paths. If Task 01 is not complete when the handoff is used, do not falsely claim completion in the commit message; either commit the explicitly requested checkpoint or finish the task first and report the exact scope.

## Suggested skills

- `opsx-implement`: continue the active Task 01 packet with public-seam tests, bounded implementation, and evidence capture.
- `opsx-verify`: perform a fresh spec-compliance and release-risk pass after Task 01 tests/build/smoke complete.
- `pi-subagents`: use only for read-only review or a tightly bounded continuation; keep one writer for `native/**`.
- `handoff`: use again only if another session boundary is needed; keep the next handoff concise and reference the existing change artifacts.
