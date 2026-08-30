# 场景标记采集实验室

## Purpose

导航用于未来场景 matcher 的内部标记采集工具。它从可见鸣潮客户区取得冻结 BGR 帧，在同一游戏屏幕矩形上提供 WPF 框选、裁剪预览和 exe-relative PNG/JSON 持久化；它不执行模板匹配，也不修改 OCR 结果或用户进度。

## Entry points

- `src/Wuwa.Core/SceneRecognition/SceneMarkerCapture.cs`：像素/归一化 ROI、显示坐标换算、最小选区、stride-aware BGR 裁剪和稳定标识校验。
- `src/Wuwa.Infrastructure/SceneMarkerStorage.cs`：`<exe>/scene-marker-lab/<scene-id>` 目录探测、PNG chunk/CRC/尺寸验证、唯一 PNG/JSON pair、取消清理和 BGR/PNG 哈希。
- `src/Wuwa.Infrastructure/SceneMarkerLabSettings.cs`：Debug 默认开启与 Release `WUWA_SCENE_MARKER_LAB` 显式 opt-in。
- `src/Wuwa.App/SceneMarkerOverlayWindow.xaml(.cs)`：冻结画面 Overlay、拖拽选区、预览、重选、保存、Esc 和保存中关闭保护。
- `src/Wuwa.App/MainWindow.xaml.cs` 的 `SceneMarkerLab_OnClick`：游戏窗口发现、主窗口/地图 Overlay 暂停、截图边界复验、Overlay 生命周期及恢复。
- `tests/Wuwa.Tests/SceneMarkerCaptureTests.cs`：坐标、裁剪、标识、开关、目录、真实 PNG、CRC、取消和 metadata 契约。

## Boundaries

- 这是 capture-only 测试工具；没有 Native/OpenCV `ISceneMatcher<OcrImageFrame>`、置信度测试、生产场景配置或 transition matrix 编辑。
- 截图基于桌面 `BitBlt`，采集前必须暂时隐藏主窗口和活动地图 Overlay，并在会话期间拒绝地图 hotkey；截图前后客户区四边必须一致。
- Overlay 使用同一冻结帧完成显示、裁剪和 PNG 编码；native `SetWindowPos` 失败时关闭流程，不保存可能错位的标记。
- 默认只写 `AppContext.BaseDirectory/scene-marker-lab`；实际 scene 子目录不可写时显式选择其他目录，不静默回退 LocalAppData，也不直接写仓库 resources。
- JSON 只描述采集 fixture；场景识别和 OCR 仍不得绕过 `OcrScanPreview` / `AchievementWorkspace.ApplyOcrPreviewAsync` 写进度。

## Read next

- 修改框选坐标、ROI 或裁剪前，先读 Core 工具和 `SceneMarkerCaptureTests`，以 source-pixel seam 为事实。
- 修改窗口发现、截图或物理屏幕定位时，继续读 `.aiknowledge/codemap/ocr-pipeline.md` 与 `WindowsGameWindowCapture.cs`；桌面 capture 会受到其他顶层窗口污染。
- 把采集模板接入真实场景判断前，继续读 `.aiknowledge/codemap/scene-transition-core.md`，在 Infrastructure 实现 matcher，不把 Native/WPF 依赖上移到 Core 状态机。
- 修改保存格式时，回读 `SceneMarkerStorage`、WPF PNG encoder 调用和真实 PNG/JSON smoke 证据。

verified_against: commit:115acba6b8cc3d44a200d2b1bf554f7368bb5949
