# Spec：achievement-tracking-floating-window

正文默认使用中文；路径、命令、配置键名保持原文。

## 意图

为 Native WPF 成就工作区增加独立的成就追踪能力，降低用户在成就库中反复查找和标记成就的成本。追踪列表必须与现有 `AchievementWorkspace` 状态和 generation 持久化保持一致。

本 change 明确只支持《鸣潮》**窗口模式**下的置顶追踪浮窗。游戏独占全屏不保证覆盖，不实现 DirectX 注入、渲染层 Hook 或游戏进程注入。

## 方案摘要

在工作区 metadata 中以 `AchievementId` 顺序列表保存追踪项；主窗口使用 DataGrid 的扩展多选批量加入追踪，并提供独立的 WPF `ToolWindow` 作为追踪面板。浮窗展示前 5 条完整追踪项，后续项使用紧凑缩略项并允许滚动；搜索框只检索状态为 `Incomplete` 的成就名称。所有“已完成”操作统一调用 `AchievementWorkspace.ChangeStatusAsync`，完成后的追踪项由 Core 自动清理，非追踪搜索结果只更新完成状态。

## 范围 / 非目标

### 范围内

- 在 Core metadata 中持久化有序的 `TrackedAchievementIds`。
- 批量加入和移除追踪项，并校验只能追踪当前存在、非 tombstone 且状态为 `Incomplete` 的成就。
- 状态变化、导入、Wiki 同步和 OCR 合并后清理不再处于 `Incomplete` 的追踪项。
- 主窗口支持使用 Ctrl/Shift 扩展选择批量加入追踪。
- 新增窗口模式下可用的置顶追踪浮窗。
- 浮窗展示前 5 条完整项目、其余项目的紧凑缩略项和垂直滚动。
- 浮窗按成就名称搜索未完成成就。
- 追踪项和搜索结果均支持“已完成”操作。
- 浮窗提供“清空追踪列表”控件；清空只删除追踪关系，不改变任何成就完成状态。
- 主窗口工作区操作按“追踪 / OCR 扫描 / 数据管理 / 外观与系统”分区展示。
- 工作区操作按钮使用圆角样式，WrapPanel 换行时保持明确的垂直间距。
- 复用现有 generation 原子保存，不修改 legacy 文件。
- Core、JSON 持久化和 WPF UI 验证。

### 范围外

- 独占全屏覆盖。
- 无边框全屏或 DirectX/游戏进程注入。
- 全局快捷键、系统托盘和跨设备同步。
- SQLite 或其他新数据库；当前 Native JSON generation 仍是权威状态。
- 搜索结果自动加入追踪。

## 可观察行为

1. 用户可以在主窗口中使用 Ctrl/Shift 选择多条成就，并一次性加入追踪；已完成、暂不可获取、已占用和 tombstone 成就不能加入。
2. 追踪列表按加入顺序保存，关闭并重新打开应用后顺序和内容保持不变。
3. 用户打开追踪功能后，主窗口隐藏并显示一个独立置顶浮窗；关闭浮窗或点击“展开工作区”后主窗口恢复。
4. 浮窗默认将前 5 条追踪成就以完整卡片显示，超过 5 条的项目以紧凑缩略项显示；列表可以向下滚动。
5. 浮窗搜索框按名称不区分大小写检索，结果只包含当前状态为 `Incomplete` 的成就。
6. 点击追踪列表中的“已完成”后，状态通过 `ChangeStatusAsync` 写入新的 generation，并从追踪列表删除该成就。
7. 点击搜索结果中未追踪成就的“已完成”后，只更新该成就状态，不改变其他追踪项。
8. 成就组状态转换导致其他追踪成员变为 `Completed` 或 `Occupied` 时，这些成员也从追踪列表清理。
9. 浮窗点击“清空追踪列表”后，追踪数量变为 0，成就状态保持不变；操作需要明确确认。
10. 主窗口工作区操作按功能主题分区，追踪、OCR、数据管理和外观/系统操作不再混排。
11. 应用已有的主题、身份映射、tombstone、legacy 文件和 generation 恢复语义不受影响。
12. 在游戏窗口模式下浮窗保持置顶可用；独占全屏不承诺覆盖。

## First-Principles Snapshot

- 真实目标：在频繁查看成就库的过程中快速维护一个可持久化的未完成成就清单。
- 最小机制：Workspace metadata 保存有序 `AchievementId` 列表；Core 统一校验、清理和保存；WPF 浮窗负责展示和触发已有状态命令。
- 边界 / 非目标：仅支持窗口模式；不做独占全屏 Overlay、Hook 或新数据库。
- 当前事实：`AchievementWorkspace` 是状态变更和 generation 保存入口；`WorkspaceMetadata.Settings` 已有 metadata 持久化链路；当前 `DataGrid` 是只读单选表格。
- 关键未知：无；窗口模式置顶浮窗和输入焦点按普通 WPF ToolWindow 实现。
- 证据门槛：Core 测试、generation round-trip、Release test/build 和窗口模式 UI smoke 通过。
- 推荐选择：在 metadata 中新增追踪字段，而不是把追踪状态编码到设置字符串或 UI 内存。

## 受影响文件 / 模块

| 路径 / 模块 | 预期动作 | 作用 / 链路 |
|---|---|---|
| `src/Wuwa.Core/Contracts.cs` | modify | 增加追踪 metadata、错误码和工作区视图快照入口所需契约。 |
| `src/Wuwa.Core/AchievementWorkspace.cs` | modify | 增加批量追踪命令、精确未完成校验、状态变化后的追踪清理和快照读取。 |
| `src/Wuwa.Core/SyncWorkspace.cs` | modify | Wiki 同步后按新成就库和状态清理追踪项。 |
| `src/Wuwa.Infrastructure/Persistence.cs` | modify | 将追踪 ID 作为 metadata 字段写入和读取 generation JSON，并兼容旧格式。 |
| `src/Wuwa.App/MainWindow.xaml` | modify | 增加批量追踪、打开浮窗入口和扩展多选。 |
| `src/Wuwa.App/MainWindow.xaml.cs` | modify | 处理批量选择、浮窗生命周期和主窗口/浮窗刷新同步。 |
| `src/Wuwa.App/AchievementTrackerWindow.xaml` | add | 窗口模式下的置顶追踪面板布局。 |
| `src/Wuwa.App/AchievementTrackerWindow.xaml.cs` | add | 追踪列表、名称搜索、完成命令和主窗口恢复。 |
| `src/Wuwa.App/TrackerItemViewModel.cs` | add | 为浮窗模板提供稳定的展示对象。 |
| `tests/Wuwa.Tests/AchievementWorkspaceTests.cs` | modify | 覆盖批量追踪、状态清理、搜索条件和成就组联动。 |
| `tests/Wuwa.Tests/PersistenceAndMigrationTests.cs` | modify | 覆盖追踪 metadata 的 generation round-trip 和旧格式兼容。 |
| `scripts/verify-ui.ps1` | modify | 增加追踪入口 AutomationId smoke 检查。 |
| `scripts/verify-tracker-ui.ps1` | add | 启动应用、打开浮窗并验证浮窗及关键控件可被 UI Automation 找到。 |

## 领域语言

- **追踪成就**：以 `AchievementId` 标识、当前状态必须为 `Incomplete` 的用户关注成就集合；不是另一种进度状态。
- **窗口模式浮窗**：普通 WPF 置顶工具窗口，只承诺在游戏窗口模式下显示；独占全屏不是支持目标。
- **搜索结果外完成**：搜索到但不在追踪 metadata 中的成就，完成时只改变进度状态，不自动加入或重排追踪列表。

## 设计 / 决策

### 关键决策

1. 追踪身份使用 `AchievementId`，避免名称、排序号或 `LegacyCode` 变化导致追踪错配。
2. 追踪列表使用有序列表而非 set，以保留浮窗前 5 条的稳定顺序。
3. Core 只允许追踪严格为 `ProgressStatus.Incomplete` 的活动成就；状态联动后的清理由 Core 统一完成。
4. 完成按钮统一复用 `ChangeStatusAsync`，保证互斥组、累计进度组和原子保存语义不被 UI 绕过。
5. 浮窗使用 `WindowStyle=None`、`Topmost=true`、`ShowInTaskbar=false` 和自定义标题栏；不设置游戏 Overlay 注入机制。标题栏支持拖动、关闭和展开工作区，内容区固定尺寸并通过滚动查看更多项目。
6. metadata 新字段按可选字段读取，旧 generation 缺少该字段时视为空列表，不修改 schema version。
7. 清空追踪复用已有批量移除命令，不增加另一套状态存储；主窗口操作区使用带标题的分区和细分隔线组织按钮。
8. 工作区操作区的按钮使用独立圆角模板和底部 margin，保证多行布局的行间空隙；不改变其它窗口的按钮布局。

### 不采用的方案

- 不把追踪 ID 编码到 `Settings` 字符串中，避免与主题等偏好混杂并失去结构校验。
- 不在 WPF code-behind 中直接修改 `_state.Statuses`，避免绕过 `AchievementWorkspace` 成就组 transition。
- 不为独占全屏实现 DLL 注入、Present Hook 或输入注入。
- 不新增 SQLite；当前 generation 已提供完整快照、原子激活和恢复能力。

## 验证计划

- `dotnet test WutheringWavesAchievement.sln -c Release`，预期：所有 Core、持久化和既有测试通过。
- `dotnet build WutheringWavesAchievement.sln -c Release -p:BuildNativeOcr=false`，预期：Native WPF 应用编译成功。
- `powershell -ExecutionPolicy Bypass -File scripts/verify-ui.ps1`，预期：主窗口追踪入口 AutomationId 可访问，UI smoke 正常结束。
- `powershell -ExecutionPolicy Bypass -File scripts/verify-tracker-ui.ps1`，预期：浮窗可打开，`TrackerWindow`、搜索框、返回/展开按钮和滚动区域可被 UI Automation 找到。
- 手动窗口模式场景：批量加入 6 条、打开浮窗、确认 5 条完整/其余缩略可滚动、搜索未追踪成就并完成、完成追踪成就后移除，预期：状态和追踪列表重启后保持一致。

## 风险 / 回退

- WPF ToolWindow 的 Topmost 行为在不同窗口管理器或多显示器环境可能不同；浮窗功能失败时不影响主工作区和状态持久化。
- 旧 generation 若包含无法解析的追踪 ID 会按现有 malformed metadata 规则拒绝加载；合法但已完成、已占用或已 tombstone 的追踪 ID 会在打开时清理并保存新 generation。
- 若 UI smoke 无法稳定控制 modeless 浮窗，保留 AutomationId 和人工窗口模式验证，不引入游戏注入或全局快捷键作为补偿方案。
