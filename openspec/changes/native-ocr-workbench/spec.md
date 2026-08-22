# Native OCR Workbench

## Intent

为 Native OCR 提供专用扫描工作窗口，使当前分类扫描和全量扫描的长时间运行状态、取消操作、警告和结果摘要可见，同时保持现有主页面结构和安全的预览后写入流程。

## Scope

### In scope

- 当前分类 OCR 和全量 OCR 共享的任务窗口入口。
- 结构化扫描进度、分类/页码信息、匹配统计、未匹配和可恢复警告。
- 用户取消、任务失败后的资源释放和主窗口状态恢复。
- 扫描完成后复用现有 OCR 预览窗口，接受后通过 `AchievementWorkspace` 写入。
- 深色/浅色主题、UI Automation 标识和无游戏窗口时的明确错误提示。

### Non-goals

- 新增顶层应用 Tab。
- 修改 Python 版。
- 修改 OCR 识别模型、模板匹配、模糊匹配、分类坐标或滚动策略。
- 扫描过程中写入 workspace。
- Tracker overlay、系统托盘或后台常驻扫描。

## Observable behavior

1. 点击“OCR 自动扫描当前分类”或“OCR 全量扫描所有分类”后，Native 打开一个明确显示扫描模式的 OCR 工作窗口；主窗口不再承担主要进度展示。
2. 工作窗口在扫描过程中显示当前阶段、当前一级/二级分类、当前页码、累计匹配数量和未匹配数量；全量扫描额外显示已访问分类数和可恢复跳过警告。
3. 工作窗口提供可用的“取消扫描”操作。取消后，扫描停止，工具窗口状态恢复，且当前 workspace revision 和进度数据不变。
4. 扫描失败时，工作窗口显示用户可理解的错误和必要的诊断提示；释放 OCR、捕获和取消资源，不留下不可再次启动的忙碌状态。
5. 扫描成功后，工作窗口显示结果摘要并打开现有 OCR 预览窗口；在用户明确接受前，workspace 不发生变化。
6. 用户接受预览后，结果通过现有 workspace OCR 应用契约写入，主页面刷新统计和列表；用户取消预览则保持原 revision。
7. 工作窗口在深色和浅色主题下均有可读对比度，不遮挡关键文本；运行状态和取消控件可通过 UI Automation 定位。
8. 现有两个 OCR 命令的识别语义和扫描范围保持不变，Python 版继续可独立运行。

## Verification

- Core coordinator contract tests cover current-category and full-scan progress ordering, cancellation, recoverable warning reporting, no-write-before-preview, and failure cleanup outcome。
- WPF UI Automation verifies both OCR buttons open the correct mode, progress/summary/cancel controls are present, and completion/cancellation returns the window to an actionable state。
- Release build and existing test suite remain green。
- Manual same-integrity Windows smoke runs one current-category scan and one full scan with the game visible, confirms the task window reports progress, cancellation restores the app, and accepted preview updates the workspace only after confirmation。
