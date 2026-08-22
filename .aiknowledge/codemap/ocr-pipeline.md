# 原生版与旧版 OCR 链路

## Purpose

导航两套 OCR 链路的边界：旧版 Python 负责当前完整的游戏分类导航/滚轮扫描；原生版以 C++ PP-OCRv5、稳定 C ABI 和 C# safe-handle 建立逐步替代路径，并将结果通过预览接入原生工作区。

## Entry points

- 旧版：`core/achievement_ocr.py`、`core/game_capture.py`、`core/ocr_tab.py`、`onnxocr/` 和 `resources/ocr_templates/`；入口包括 `scan_current_page`、`scan_with_scroll`、`scan_all_tabs`。
- 原生 C++：`native/ocr/include/wuwa_ocr.h`、`native/ocr/src/ocr_engine.cpp`、`detection.cpp`、`classifier.cpp`、`exports.cpp`；负责 PP-OCRv5 检测/分类/识别、BGR 处理、结果缓冲区和 ABI 错误。
- 原生托管边界：`native/src/Wuwa.Infrastructure/NativeOcrClient.cs`、`NativeOcrTextReader.cs`、`WindowsGameWindowCapture.cs`；负责 safe handle、模型路径、Windows 捕获、点击/滚轮和串行推理。
- 原生领域边界：`native/src/Wuwa.Core/OcrScanContracts.cs`、`OcrMatching.cs`、`AchievementWorkspace.ApplyOcrPreviewAsync`；负责图像帧/文本行契约、名称归一化、Levenshtein 匹配、状态配对和确认后合并。
- Tests/runtime：`native/tests/Wuwa.Tests/NativeOcrIntegrationTests.cs`、`OcrMatchingTests.cs`、`OcrScanServiceTests.cs`、`WindowsGameWindowCaptureSmokeTests.cs`；构建 `native/scripts/build-native-ocr.ps1`，界面/便携 smoke 见 `verify-ui.ps1` 和 `verify-portable-lifecycle.ps1`。

## Boundaries

- C++ 动态库不写用户进度；它只拥有 OCR 会话、模型/字典验证和结构化识别结果。C# 包装器串行化共享句柄的推理调用。
- 捕获/输入是 Windows x64 边界，必须发现可见客户区、验证尺寸、聚焦窗口并处理取消/失败；Python 与原生版的桌面输入细节不能靠抽象名称猜测。
- OCR 匹配器输出预览候选、未匹配和歧义信息；工作区在用户确认前不激活 revision，并应用完成状态防降级和成就组状态转换。
- 原生版全局分类扫描仍是独立的后续边界；当前文档/代码不能把已完成的单页或当前分类能力宣称为全局 parity。

## Read next

- 先读 `native/ocr/README.md` 和 `openspec/changes/native-ocr-scan/spec.md`，确认 ABI、模型、发布和 parity 约束。
- 需要匹配规则时对照 `core/achievement_ocr.py` 与 `native/src/Wuwa.Core/OcrMatching.cs`。
- 需要输入/滚轮问题时读 `WindowsGameWindowCapture.cs`、`core/game_capture.py`、`resources/config.ini` 和 `test_scroll.py`。
- 需要扫描写入边界时读 `OcrScanContracts.cs`、`AchievementWorkspace.cs` 和 `OcrMatchingTests.cs`。
- `verified_against: commit:94aeb30`
