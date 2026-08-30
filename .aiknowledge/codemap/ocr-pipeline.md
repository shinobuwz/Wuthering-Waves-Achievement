# 原生 OCR 链路

## Purpose

导航 Native WPF/.NET 8 应用的 PP-OCRv5、游戏窗口捕获、OCR 专用输入控制、分类导航和结果合并边界。Native 是当前唯一维护的应用运行时；Rotation 的只读输入观察是独立边界。

## Entry points

- 原生 C++：`ocr/include/wuwa_ocr.h`、`ocr/src/ocr_engine.cpp`、`detection.cpp`、`classifier.cpp`、`exports.cpp`；负责 PP-OCRv5 检测/分类/识别、BGR 处理、结果缓冲区和 ABI 错误。
- 原生托管边界：`src/Wuwa.Infrastructure/NativeOcrClient.cs`、`NativeOcrTextReader.cs`、`WindowsGameWindowCapture.cs`；负责 safe handle、模型路径、Windows 捕获、OCR 点击/滚动和串行推理。
- 原生领域边界：`src/Wuwa.Core/OcrScanContracts.cs`、`OcrMatching.cs`、`AchievementWorkspace.ApplyOcrPreviewAsync`；负责图像帧/文本行契约、名称归一化、Levenshtein 匹配、状态配对和确认后合并。
- WPF 协调入口：`src/Wuwa.App/AchievementWorkspaceView.xaml` 提供 OCR 控件，`MainWindow.xaml.cs` 协调当前分类/全量分类命令、分类遍历、进度、取消及最终预览。
- Tests/runtime：`tests/Wuwa.Tests/`、`ocr/tests/`；构建 `scripts/build-native-ocr.ps1`，界面/便携 smoke 见 `verify-ui.ps1` 和 `verify-portable-lifecycle.ps1`。

## Boundaries

- C++ 动态库不写用户进度；它只拥有 OCR 会话、模型/字典验证和结构化识别结果。C# 包装器串行化共享句柄的推理调用。
- OCR 捕获/输入是 Windows x64 边界，必须发现可见客户区、验证尺寸、聚焦窗口并处理取消/失败；API 返回成功不等于游戏界面已经发生可见变化，必须结合截图和日志验证。
- OCR 发送输入只属于 `WindowsGameWindowCapture` 与扫描协调路径；Native 连招助手不得依赖这些输入发送方法。
- OCR 匹配器输出预览候选、未匹配和歧义信息；工作区在用户确认前不激活 revision，并应用完成状态防降级和成就组状态转换。
- 全量分类扫描必须对一级/二级导航、重复 OCR 文本、滚动终点和无变化轮次设置有界行为，避免漏扫、重复点击和死循环。

## Read next

- 先读 `ocr/README.md`、C ABI 头文件和 Native OCR 构建/测试脚本，确认 ABI、模型和发布约束。
- 需要匹配规则时读 `src/Wuwa.Core/OcrMatching.cs` 与相关测试。
- 需要输入/滚动问题时读 `src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs`、`src/Wuwa.App/MainWindow.xaml.cs` 和 `<程序目录>\log\native-ocr-YYYY-MM-DD.log`。
- 需要扫描写入边界时读 `OcrScanContracts.cs`、`AchievementWorkspace.cs` 和 `OcrMatchingTests.cs`。
- 需要只读连招 Hook 时不要沿 OCR 输入路径继续，转读 `codemap/rotation-assistant.md`。
- `verified_against: commit:2486e5a5bedb0bc23468d152c08a1e43031d96a1`
