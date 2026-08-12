# Evidence

### Four-change migration instead of one cutover | plan
背景：The requested rewrite spans desktop UI, persistence/schema migration, a native OCR ABI, game automation, overlay window behavior, and release removal.
事件：Repository inspection and independent plan review showed that combining these surfaces would hide data-loss and reconciliation failures until final cutover.
结论：Use four serial changes: native workspace, native OCR, tracker overlay, then native cutover. Only the first change is active here.

### Preserve VS2022 by targeting .NET 8 | plan review
背景：The draft paired Visual Studio 2022 with `net10.0-windows`.
事件：Independent review identified that this is not a supported IDE/toolchain contract even though the installed CLI can build a .NET 10 WPF probe.
结论：Pin the native app to `net8.0-windows` and an explicit SDK through `global.json`; verify both VS2022 and CLI builds.

### Replace mutable legacy codes with AchievementId | plan review
背景：Legacy progress is keyed by `编号`, while category re-encoding can regenerate codes and Wiki fields can change.
事件：The review found duplicate achievement names and no defined reconciliation behavior for rename, description/category change, ambiguous matches, or removals. Live Wiki inspection confirmed row source references exist as table UID plus row index but may themselves drift when a table is rebuilt.
结论：Introduce immutable `AchievementId`, retain `LegacyCode`, store `WikiSourceRef`, reconcile conservatively, quarantine ambiguity, and tombstone removals.

### One-way legacy import | plan review
背景：The draft kept Python available while migrating one profile into native state.
事件：The review demonstrated that hidden or bidirectional synchronization would fork state when either application changes after initial import.
结论：Legacy files are read-only inputs. Native import is explicit and reviewable; re-import is an explicit replace with rollback. No watcher or automatic two-way merge is implemented.

### Versioned aggregate generations | plan review
背景：Library, category, progress, identity mapping, settings, and migration state must remain mutually consistent.
事件：Per-file atomic replacement cannot guarantee a consistent multi-file revision and the existing Python order can expose mismatched base/progress state.
结论：Persist complete validated generations and atomically replace one current-manifest pointer. Retain prior valid generations and fault-test every activation boundary.

### Domain glossary deferred | plan
背景：`AchievementId`, `Tracked Achievement`, and `Tracker Overlay` are stable cross-change terms, but this repository does not yet contain the required `.aiknowledge/README.md` contract surface.
事件：Plan inline domain writing is bounded by that repository knowledge contract.
结论：Keep canonical terms in this spec and defer `.aiknowledge/domain.md` creation to verified knowledge finalization rather than inventing a partial knowledge root.

## Final Verification

覆盖范围：Task 01–06 native side-by-side workspace implementation under `native/**`: workspace behavior, transactional generations/recovery, explicit read-only legacy import, anonymous Wiki adapter/reconciliation seam, JSON and 12-column TSV/Excel-compatible exchange, dark/light shell controls, update checker, and self-contained publish configuration.
验证命令：`dotnet restore native/WutheringWavesAchievement.sln`；`dotnet test native/WutheringWavesAchievement.sln --no-restore`（17 passed）；`dotnet build native/WutheringWavesAchievement.sln -c Release --no-restore`（0 warnings/0 errors）；`dotnet restore native/src/Wuwa.App/Wuwa.App.csproj -r win-x64`；`dotnet publish native/src/Wuwa.App/Wuwa.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o native/publish/win-x64`（passed）；published executable bounded launch smoke with temporary `WUWA_NATIVE_DATA_ROOT` kept alive for 5 seconds and created `current.json`；`git diff --check`（passed）。
spec 合规：WPF 默认启动使用 `JsonAppDataStore`，首次启动先尝试 legacy discovery；旧版配置与进度只读；native generation 使用 atomic manifest 且可从保留 generation 恢复；workspace 对 status/import/sync/exchange 均保留失败前快照；资源经 infrastructure adapter 读取；publish 配置为 `win-x64` self-contained。
release risk：当前 automated UI Automation、真实 live Wiki probe、截图人工检查和 Windows 安装/卸载生命周期尚未在此环境中完成；Excel 使用无第三方依赖的 TSV 兼容实现而不是原生 `.xlsx` 库。Wiki 远程业务字段的实际 schema 仍需现场 fixture/live probe 验证。
结论：native solution、行为测试、Release build 和 self-contained win-x64 publish 均通过；可进入用户验收，但上述 UI/live/package lifecycle 风险需在验收中重点检查。
