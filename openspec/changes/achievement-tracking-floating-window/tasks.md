# Tasks：achievement-tracking-floating-window

正文默认使用中文；路径、命令保持原文。

## 独立任务文档索引

本 change 使用垂直切片任务；实现细节以内联验收为准，不创建重复的横向任务文档。

## 1. Core 追踪状态与原子持久化

- [x] 1.1 增加 `TrackedAchievementIds` metadata、批量加入/移除追踪命令、严格未完成校验、状态变化后的统一清理，并将其接入 Open/导入/Wiki/OCR 状态链路。
  - Blocked by：无
  - Suggested Files：`src/Wuwa.Core/Contracts.cs`、`src/Wuwa.Core/AchievementWorkspace.cs`、`src/Wuwa.Core/SyncWorkspace.cs`
  - 验收：追踪列表以 `AchievementId` 和加入顺序保存；非 `Incomplete`、tombstone 或未知项目不能加入；完成及成就组联动后自动清理；所有变化仍通过 `IAppDataStore.SaveAsync` 生成新 revision。
  - 验证：`dotnet test WutheringWavesAchievement.sln -c Release --filter FullyQualifiedName~AchievementWorkspaceTests`，预期：新增追踪、状态清理和成就组测试通过。

## 2. Generation JSON 兼容

- [x] 2.1 扩展 `MetadataDocument` 读写追踪 ID，并覆盖 round-trip、旧 generation 缺失字段和故障恢复语义。
  - Blocked by：1.1
  - Suggested Files：`src/Wuwa.Infrastructure/Persistence.cs`、`tests/Wuwa.Tests/PersistenceAndMigrationTests.cs`
  - 验收：重启后追踪顺序和状态保持一致；旧 metadata 无追踪字段时按空列表打开；追踪与状态在同一个 generation 中原子激活。
  - 验证：`dotnet test WutheringWavesAchievement.sln -c Release --filter FullyQualifiedName~PersistenceAndMigrationTests`，预期：持久化和恢复测试通过。

## 3. 主窗口批量入口与追踪浮窗

- [x] 3.1 将成就表改为扩展多选，增加批量加入追踪入口，并实现窗口模式下的置顶追踪 ToolWindow：前 5 条完整、后续缩略、可滚动、按名称搜索和完成按钮。
  - Blocked by：1.1、2.1
  - Suggested Files：`src/Wuwa.App/MainWindow.xaml`、`src/Wuwa.App/MainWindow.xaml.cs`、`src/Wuwa.App/AchievementTrackerWindow.xaml`、`src/Wuwa.App/AchievementTrackerWindow.xaml.cs`、`src/Wuwa.App/TrackerItemViewModel.cs`
  - 验收：主窗口 Ctrl/Shift 可批量选择；浮窗可打开/关闭并恢复主窗口；追踪和搜索完成按钮按 Core 规则刷新；“清空追踪列表”需要确认且不改变成就状态；标题栏无系统边框并可拖动关闭；主窗口操作按钮按功能主题分区；游戏窗口模式下浮窗保持置顶；不声称支持独占全屏。
  - 验证：`dotnet build WutheringWavesAchievement.sln -c Release -p:BuildNativeOcr=false`，并执行人工窗口模式场景。

## 4. UI Smoke 与完整回归

- [x] 4.1 增加追踪控件 AutomationId 和 UI smoke 检查，运行全量测试与 Release 构建，记录最终验证摘要。
  - Blocked by：3.1
  - Suggested Files：`scripts/verify-ui.ps1`、`scripts/verify-tracker-ui.ps1`、`tests/Wuwa.Tests/`
  - 验收：主窗口和浮窗入口可被 UI Automation 找到；全量测试、构建和 UI smoke 通过；不修改 legacy 文件。
  - 验证：`dotnet test WutheringWavesAchievement.sln -c Release`、`dotnet build WutheringWavesAchievement.sln -c Release -p:BuildNativeOcr=false`、`powershell -ExecutionPolicy Bypass -File scripts/verify-ui.ps1`、`powershell -ExecutionPolicy Bypass -File scripts/verify-tracker-ui.ps1`。
