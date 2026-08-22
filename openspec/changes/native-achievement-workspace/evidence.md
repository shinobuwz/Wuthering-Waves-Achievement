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

## Final Verification — 2026-08-12

覆盖范围：Task 01–06 的可自动化部分，包括 workspace contracts、事务 generations/recovery、显式只读 legacy import、匿名 Wiki 解析与 reconciliation、JSON/TSV/原生 OOXML `.xlsx` exchange、深浅主题、GitHub update fixtures、self-contained publish 和 portable lifecycle。

### Commands and results

- `dotnet test WutheringWavesAchievement.sln -c Release --no-restore`：52 passed，3 skipped，0 failed。默认跳过项为需要显式环境的 native OCR integration、live Wiki 和 window capture smoke；其中 live Wiki 已单独执行。
- `dotnet build WutheringWavesAchievement.sln -c Release --no-restore`：0 warnings，0 errors。
- `powershell -ExecutionPolicy Bypass -File scripts/verify-wiki-live.ps1`：1 passed；使用临时 data root，两次真实匿名同步通过，第二次等价内容不推进 revision。
- `powershell -ExecutionPolicy Bypass -File scripts/verify-ui.ps1`：UI Automation 找到主要操作控件，并生成四张非空截图：
  - `1080x700-dark.png` SHA-256 `ae8f537ed7d85d9f197b766a4b1d470e3ef773287a9088b1b81527dd59e69447`
  - `1080x700-light.png` SHA-256 `4ecaa5f00db1583d14b3229eaeb78864324ae12ab7a0a2ead8bdb32478c999c2`
  - `1440x900-dark.png` SHA-256 `fa913cff0b071e552582965624ee13b8389a627566b81d82424a7a2305eb1c63`
  - `1440x900-light.png` SHA-256 `3c9f8792ae812baa9dd30a91fd9ca87aedc092d85ff5fe8a25d47a3402f18b3c`
- `powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Configuration Release -SkipTests`：CMake/MSVC OCR build、CTest 2/2、self-contained `win-x64` publish、required/forbidden package checks 和 package manifest 通过。输出目录被限制在 `publish/`，使用 staged replacement。
- `scripts/verify-portable-lifecycle.ps1`：同一发布包的 portable launch/relaunch、alternate-copy 启动、删除程序目录和重新部署状态保留模拟通过；不把它表述为不同版本升级测试。
- 同脚本使用复制的 legacy config/progress fixture：自动导入 profile metadata/已完成状态、legacy hash 不变、upgrade/uninstall/reinstall state retention 通过。
- `git diff --check`：passed。

### Compliance and residual risk

- Production UI 只通过 `AchievementWorkspace`/infrastructure adapters 访问状态和系统边界。
- Legacy files 是只读输入；未知 legacy code 会拒绝整个导入，避免静默部分迁移。
- generation 只在 manifest replacement 后成为 current；fault injection 覆盖 pre/post commit，并防止 corrupt manifest 激活未提交 orphan。
- Wiki HTTP、business status、hierarchy/source refs、plausible count 和 ambiguity 均校验；bootstrap fallback 只在本地库尚无 WikiSourceRef 时启用。
- Exchange 提供结构化 diagnostics；非 replacement progress import 保留未指定状态。
- Update release URL 限制为目标 GitHub repository；网络结果不依赖非权威 cache 写入成功。
- `.xlsx` 为直接读写 OOXML，不再只是 TSV 兼容层。

仍需用户参与：对四张截图/实际界面的主观视觉和操作体验确认；确认 portable lifecycle 是否足够，或是否另行要求 MSI/MSIX/Setup.exe。仍未完成完整的端到端 UI Automation（filter/status/stat/theme/error 场景）、所有高 DPI、屏幕阅读器和复杂对话框的人工作业；Task 05 因此保持未勾选。Wiki 原始响应 cache 仍为非权威优化项，当前 correctness/live probe 不依赖该 cache。

结论：除 Task 05 的用户主观验收和“是否要求真实安装器”的产品决定外，可自主完成的 workspace 收尾、测试、live probe、截图、publish 和 portable lifecycle 已完成。Change 尚未归档。
