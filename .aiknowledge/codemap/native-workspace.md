# 原生版工作区

## Purpose

导航原生版 WPF/.NET 8 的公共工作区入口、状态模型、查询/统计、状态转换、旧版导入、Wiki 同步、交换和事务化持久化。原生版行为不应由 WPF code-behind 或适配器私自重建。

## Entry points

- `native/src/Wuwa.App/MainWindow.xaml`、`MainWindow.xaml.cs`：WPF 表格、筛选、状态命令、主题、交换和 OCR 界面；通过工作区调用领域行为。
- `native/src/Wuwa.Core/AchievementWorkspace.cs`：打开、查询、统计、状态转换、旧版导入、交换、Wiki 同步和 OCR 预览合并的最高公共入口。
- `native/src/Wuwa.Core/Models.cs`、`Contracts.cs`、`ExchangeContracts.cs`、`SyncContracts.cs`：成就身份、ProgressStatus、WorkspaceState/Snapshot、适配器契约和结构化错误。
- `native/src/Wuwa.Infrastructure/Persistence.cs`：generation、manifest、commit marker、恢复和保留策略；同目录还包含 shipped library、旧版、Wiki、交换和 Win32 适配器。
- Tests/runtime：`native/tests/Wuwa.Tests/`；`dotnet test native/WutheringWavesAchievement.sln -c Release`；发布/便携 smoke 见 `native/scripts/publish-native.ps1` 和 `verify-portable-lifecycle.ps1`。

## Boundaries

- WPF 不直接读取 JSON、调用 Wiki HTTP 或修改旧版文件；这些操作经过 Core 契约和 Infrastructure 适配器。
- Core 以 `AchievementWorkspace` 持有当前状态，并在成功命令后推进 revision；失败返回结构化错误，旧 revision 保持有效。
- 原生版可变状态默认在 `%LocalAppData%\\WutheringWavesAchievement`，随程序发布的 `resources/` 作为只读成就库/资源输入；旧版配置和档案只读导入。
- `AchievementId`、成就组状态转换、统计和 `ProgressStatus` 是领域规则；界面、OCR、Wiki 和交换都必须复用它们。

## Read next

- 先读 `native/src/Wuwa.Core/Models.cs`、`Contracts.cs`，建立身份/状态/快照语义。
- 修改工作区行为时读 `AchievementWorkspace.cs` 的全部 partial 文件和 `native/tests/Wuwa.Tests/AchievementWorkspaceTests.cs`。
- 修改持久化或迁移时读 `Persistence.cs`、`LegacyProfileSources.cs`、`PersistenceAndMigrationTests.cs`。
- 修改远端同步/交换时读 `SyncWorkspace.cs`、`WikiSources.cs`、`AchievementExchangeFactory.cs` 和 `WikiExchangeUpdateTests.cs`。
- `verified_against: commit:94aeb30`
