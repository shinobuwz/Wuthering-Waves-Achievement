# 原生版工作区

## Purpose

导航 Native WPF/.NET 8 的模块化应用壳、成就公共工作区、状态模型、查询/统计、状态转换、旧版导入、Wiki 同步、交换和事务化持久化。壳层只负责页面路由与全局协调，成就行为不应由 WPF code-behind 或适配器私自重建。

## Entry points

- `src/Wuwa.App/MainWindow.xaml`、`MainWindow.xaml.cs`：Dashboard-first 左侧导航、页面生命周期、主题、全局快捷键和跨模块协调。
- `src/Wuwa.App/AchievementWorkspaceView.xaml`：成就搜索、筛选、状态、OCR、追踪、Wiki 和交换的视觉 namescope；命令仍由 MainWindow 协调到工作区。
- `src/Wuwa.Core/AchievementWorkspace.cs`：打开、查询、统计、状态转换、旧版导入、交换、Wiki 同步和 OCR 预览合并的最高公共入口。
- `src/Wuwa.Core/Models.cs`、`Contracts.cs`、`ExchangeContracts.cs`、`SyncContracts.cs`：成就身份、ProgressStatus、WorkspaceState/Snapshot、适配器契约和结构化错误。
- `src/Wuwa.Infrastructure/Persistence.cs`：generation、manifest、commit marker、恢复和保留策略；同目录还包含 shipped library、旧版、Wiki、交换和 Win32 适配器。
- Tests/runtime：`tests/Wuwa.Tests/`；`dotnet test WutheringWavesAchievement.sln -c Release`；发布/便携 smoke 见 `scripts/publish-native.ps1` 和 `scripts/verify-portable-lifecycle.ps1`。

## Boundaries

- WPF 模块视图不直接读取 JSON、调用 Wiki HTTP 或修改旧版文件；这些操作经过 Core 契约和 Infrastructure 适配器。
- Core 以 `AchievementWorkspace` 持有成就当前状态，并在成功命令后推进 revision；失败返回结构化错误，旧 revision 保持有效。
- Native 可变状态默认在 `<程序目录>\data`，`WUWA_NATIVE_DATA_ROOT` 可覆盖测试/显式运行位置；随程序发布的 `resources/` 是只读输入。
- 成就 generation 与 `rotations/` 连招流程/绑定互相独立；连招模块的源码导航见 Rotation codemap。
- `AchievementId`、成就组状态转换、统计和 `ProgressStatus` 是成就领域规则；界面、OCR、Wiki 和交换都必须复用它们。

## Read next

- 先读 `src/Wuwa.App/MainWindow.xaml.cs` 的 `NavigateTo` 与 `src/Wuwa.Core/Models.cs`、`Contracts.cs`，建立壳路由和身份/状态/快照语义。
- 修改成就行为时读 `AchievementWorkspace.cs` 的全部 partial 文件和 `tests/Wuwa.Tests/AchievementWorkspaceTests.cs`。
- 修改持久化或迁移时读 `Persistence.cs`、`LegacyProfileSources.cs`、`PersistenceAndMigrationTests.cs`。
- 修改远端同步/交换时读 `SyncWorkspace.cs`、`WikiSources.cs`、`AchievementExchangeFactory.cs` 和 `WikiExchangeUpdateTests.cs`。
- 修改连招流程、只读 Hook 或浮窗时转读 `codemap/rotation-assistant.md`。
- `verified_against: commit:2486e5a5bedb0bc23468d152c08a1e43031d96a1`
