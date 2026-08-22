## 上下文

当前 `MainWindow.xaml.cs` 同时承担以下职责：

- 检查 OCR 资产和游戏进程
- 创建捕获器、OCR reader 和扫描服务
- 执行当前分类分页扫描
- 执行全量一级/二级分类导航和分页扫描
- 更新 `HintText`
- 处理取消、恢复窗口、异常弹窗
- 打开 OCR 预览并提交 workspace

这使主窗口事件处理器过长，也让 OCR 运行状态只能显示在主页面底部。全量扫描期间工具主窗口还会最小化以避免遮挡游戏，因此用户无法持续看到有意义的进度反馈。

## 目标

- 为 OCR 长任务提供独立、可恢复、可取消的工作面板。
- 将进度报告从 `HintText` 提升为结构化事件，支持当前扫描位置和累计统计。
- 复用现有识别、导航、预览、事务写入实现，避免借机改变 OCR 识别规则。
- 保持不新增顶层应用 Tab 的决定。

## 非目标

- 不改变 Python OCR 实现。
- 不改变一级/二级分类坐标、滚动策略、终止条件或匹配规则。
- 不在扫描过程中直接写入 workspace。
- 不在本 Change 中实现 tracker overlay、后台常驻或系统托盘。
- 不要求将现有 `OcrPreviewWindow` 重写成扫描窗口；第一阶段在任务结束后继续打开现有预览窗口。

## 设计决策

### 1. 使用专用 `OcrScanWindow`，而不是恢复顶层 OCR Tab

窗口作为 `MainWindow` 的 owner，以模态或受控的 modeless 方式启动。它包含：

- 扫描模式和当前阶段
- 当前一级分类 / 二级分类 / 页码
- 已发现分类数、已扫描页数、已匹配成就数、未匹配文本数
- 最近进度消息和可恢复警告列表
- “取消扫描”按钮
- 扫描结束后的结果摘要

开始扫描后，窗口可以保持在任务栏可恢复状态，或按现有行为最小化以避免遮挡游戏；恢复后仍能看到完整进度和取消入口。默认不设置 Topmost，避免遮挡游戏输入。

### 2. 将扫描执行抽象为可报告进度的 coordinator

新增一个面向应用层的扫描入口，例如 `OcrScanCoordinator`，接收：

- `OcrScanMode`（当前分类 / 全量分类）
- workspace 当前行快照
- `CancellationToken`
- `IProgress<OcrScanProgress>` 或等价事件回调

输出 `OcrScanRunResult`，包括：

- 是否成功、是否取消
- 合并后的 `OcrScanPreview`
- 扫描页数和识别行数
- 已访问/跳过的分类
- 未匹配文本和可恢复警告
- 错误码与用户可读消息

`MainWindow` 只负责创建窗口、传递依赖、在结果接受后刷新列表；不再负责逐页更新 UI 文本。

### 3. 进度事件使用不可变快照

每次阶段变化报告一个不可变的进度记录，至少包含：

```text
Mode
Phase
PrimaryCategory
SecondaryCategory
Page
VisitedCategoryCount
TotalCategoryCount（未知时允许为空）
MatchedCount
UnmatchedCount
Message
Warning（可选）
```

UI 只消费这些快照，不读取 coordinator 的私有状态。这样可以用 fake coordinator 做不依赖游戏窗口的测试。

### 4. 取消和窗口恢复保持现有安全语义

- 点击取消只触发 `CancellationTokenSource.Cancel()`。
- coordinator 在分类切换、分页、滚动和 OCR 调用之间检查取消。
- 取消/失败时恢复工具窗口状态，释放 OCR client、capture 和 token source。
- 取消/失败不调用 `ApplyOcrPreviewAsync`，当前 workspace revision 不变。
- 成功后先打开现有 `OcrPreviewWindow`，只有明确接受才应用结果。

### 5. 主题和可访问性

- 工作窗口使用 `DynamicResource`，跟随当前深色/浅色主题。
- 控件提供明确的 AutomationId：模式、阶段、进度、取消、警告和结果摘要。
- 取消按钮在运行时保持可用；扫描完成、取消或失败后恢复为关闭/查看结果状态。

## 预期文件方向

- `native/src/Wuwa.App/OcrScanWindow.xaml`
- `native/src/Wuwa.App/OcrScanWindow.xaml.cs`
- `native/src/Wuwa.App/MainWindow.xaml.cs`
- `native/src/Wuwa.Core/OcrScanContracts.cs` 或新的 coordinator/contracts 文件
- `native/tests/Wuwa.Tests/` 下新增 coordinator/进度/取消契约测试

实际文件位置可在实现阶段根据现有 OCR 类型依赖调整，但不应把扫描算法重新复制一份。
