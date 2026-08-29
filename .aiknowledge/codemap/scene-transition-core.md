# 场景切换内核

## Purpose

导航不依赖 WPF、OCR、Native/OpenCV 或具体游戏场景的泛型场景切换 Core。它负责有序候选、首个命中、known/unknown 防抖、显式 Handler、取消、有序队列和异步重置，不负责图像算法或业务状态写入。

## Entry points

- `src/Wuwa.Core/SceneRecognition/SceneRecognitionContracts.cs`：`SceneTransitionOptions`、`SceneMatch`、`ISceneMatcher<TFrame>`、`ISceneHandler<TFrame>` 和逐帧结果契约。
- `src/Wuwa.Core/SceneRecognition/SceneTransitionEngine.cs`：`ProcessAsync`、`ResetAsync`、候选执行、pending/confirmation、Handler 调用和队列边界。
- `src/Wuwa.Core/SceneRecognition/README.md`：最小接入示例和当前非目标。
- `tests/Wuwa.Tests/SceneTransitionEngineTests.cs`：候选顺序、防抖、Handler、取消、并发、重置、重入和配置契约。

## Boundaries

- transition matrix 的候选顺序是行为契约；matcher 首个命中后停止，未命中产生 synthetic unknown。
- Handler 只对真实 matcher 命中调用；未注册 Handler 走通用 fallback。
- `ProcessAsync` 与 `ResetAsync` 共用有序队列；取消不提交半完成状态，matcher/Handler 对同一引擎的重入排队立即失败。
- Core 使用泛型 `TFrame`，不得反向依赖 `Wuwa.Infrastructure`、WPF、`OcrImageFrame`、`AchievementWorkspace` 或鸣潮具体场景。
- 当前没有生产 matcher、场景模板或 WPF/OCR 接入；OCR 结果仍必须先形成预览，再由工作区显式确认合并。

## Read next

- 修改状态机前先读 Core README、两个实现文件和对应测试，以 public `ProcessAsync` seam 为行为事实。
- 接入截图/模板识别时继续读 `.aiknowledge/codemap/ocr-pipeline.md` 指向的 `OcrImageFrame`、Native OCR 和窗口捕获源码，但不要把适配层依赖上移到 Core。
- 若需要把识别结果写入成就进度，继续读取 OCR preview 和 `AchievementWorkspace.ApplyOcrPreviewAsync` 边界。

verified_against: commit:bcd37ba478d3e32f95d697b4011ae0caf1748ecd
