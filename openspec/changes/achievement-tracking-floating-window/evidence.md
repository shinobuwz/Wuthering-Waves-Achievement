# Evidence：achievement-tracking-floating-window

正文默认使用中文；路径、命令保持原文。

`evidence.md` 是稀疏决策/失败日志加一份当前最终验证摘要。仅当事件改变了 spec/design/scope/tasks、证伪了关键假设、导致回退或方案替换、或产生改变后续推理的结论时才记录。不记录常规 red/green 迭代、瞬时命令/工具失败、时间戳、reviewer 编排或已就地修正的错误。本文件不影响路由。

## Final Verification

结论：通过。

- `dotnet test WutheringWavesAchievement.sln -c Release --no-restore`：66 passed，3 skipped，0 failed。
- `dotnet build WutheringWavesAchievement.sln -c Release -p:BuildNativeOcr=false --no-restore`：0 warnings，0 errors。
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-ui.ps1`：主窗口 UI smoke 通过。
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-tracker-ui.ps1`：`TrackerWindow`、搜索框、清空追踪、展开/关闭、返回按钮和滚动区域可访问。
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-ui.ps1`：主窗口分组操作区 UI smoke 通过。
- `git diff --check`：通过。

残余风险：尚未在真实游戏窗口模式中执行人工长时间使用；独占全屏覆盖已明确不属于本 change 支持范围。

## Decisions

- 追踪浮窗最终只支持游戏窗口模式；用户确认不实现独占全屏覆盖、DirectX 注入或游戏进程 Hook。
- 追踪状态作为 Native generation metadata 持久化，不引入 SQLite。
- 批量选择采用 WPF DataGrid Ctrl/Shift 扩展多选，不新增 UI 选择状态到 Core `AchievementRow`。
- 根据 UI 验收反馈，浮窗改为 `WindowStyle=None` 的无边框固定窗口，增加自定义标题栏、拖动和关闭按钮；内容继续通过滚动查看，不增加独占全屏支持。
- 根据后续 UI 反馈，浮窗增加带确认的“清空追踪列表”操作；主窗口工作区按钮按追踪、OCR 扫描、数据管理、外观与系统四类分组。
- 根据分组截图反馈，移除操作区内部嵌套卡片，改为单一标题栏、细分隔线和无背景的四列功能分区，降低视觉噪音。
- 根据后续截图反馈，工作区操作按钮改为圆角模板并增加 8px 底部间距，确保多行按钮之间有明确空隙。

## Failures / Rollbacks

