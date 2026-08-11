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

覆盖范围：Not yet verified; implementation has not started.
验证命令：Pending.
spec 合规：Pending.
release risk：Pending.
结论：Plan ready for implementation after independent review findings were dispositioned in `spec.md` and `tasks.md`.
