# Spec：新增可复用的场景切换 Core

## 意图

当前 Native WPF/.NET 8 项目已经具备 Windows 游戏窗口捕获、BGR 图像帧、Native OpenCV 模板匹配、PP-OCRv5、帧差分析和 OCR 预览合并能力，但缺少一套独立于具体图像算法和鸣潮业务的场景切换核心。

本 change 在 `Wuwa.Core` 中新增泛型场景切换引擎，固定有序候选、首个命中、场景防抖和显式 Handler 契约。首版只建立可复用 Core 和自动测试，不接入现有 OCR、窗口捕获或 WPF 运行流程。

## 术语

- **稳定场景**：已经通过确认阈值、由场景切换引擎对外暴露的当前场景。
- **检测场景**：matcher 对当前帧首个命中的候选场景；尚未必然成为稳定场景。
- **候选顺序**：当前稳定场景在 transition matrix 中对应的有序候选列表；顺序属于行为契约。
- **场景 Handler**：某候选场景命中后执行调用方专属逻辑的显式注册回调。

这些是本 change 的实现契约，不新增项目级 Domain Language。

## 范围

### 范围内

- 在 `src/Wuwa.Core/SceneRecognition/` 新增泛型场景切换契约和引擎。
- 支持调用方提供初始场景、unknown 场景、有序 transition matrix 和确认阈值。
- 按候选顺序逐个调用 matcher，并在首个命中后停止。
- 未命中任何候选时生成 unknown 检测结果。
- 已知场景默认 1 帧确认；unknown 默认连续 2 帧确认；两个阈值可配置。
- 场景每次命中时调用显式注册 Handler，并向 Handler 暴露本帧是否确认转场。
- 未注册 Handler 的场景继续完成通用匹配和状态切换。
- 支持取消、串行帧处理、异步重置和不可变的逐帧结果。
- 增加单元测试和最小使用说明。

### 范围外

- 新增或修改 Native/OpenCV 模板匹配 ABI。
- 实现 `ISceneMatcher<OcrImageFrame>` 的生产适配器。
- 新增场景图片、阈值、ROI 或鸣潮具体场景配置。
- 接入 `MainWindow`、OCR 工作台、全量扫描或窗口捕获循环。
- 修改 `AchievementWorkspace`、OCR 预览、generation 或用户进度。
- 建立后台常驻场景识别服务。
- 修改现有 OCR、输入、滚动、导航或发布资源协议。

## 行为要求

### R1 — 最高层 public behavior seam

首版以一个最高层 seam 为主：

```csharp
await SceneTransitionEngine<TFrame>.ProcessAsync(frame, cancellationToken)
```

调用返回本帧的检测场景、前后稳定场景、是否确认转场、pending 状态、已评估候选、命中置信度和 Handler 调用状态。

### R2 — 有序候选与首个命中

引擎必须按当前稳定场景对应的 transition matrix 顺序逐个调用 matcher。首个 `IsMatch == true` 的候选成为本帧检测场景，后续候选不得继续执行。

候选顺序不得在内部排序、去重或按置信度重排。配置中的重复候选应在构造时拒绝，而不是静默修改。

### R3 — unknown 与场景防抖

- 没有候选命中时，本帧检测场景为配置的 unknown 场景。
- 已知场景默认第一次命中即成为稳定场景。
- unknown 默认需要连续 2 帧检测才成为稳定场景。
- 若 pending 期间重新检测到当前稳定场景，应清除 pending 状态。
- 若 pending 目标改变，应从新目标的第 1 帧重新计数。
- 已知与 unknown 阈值均可配置，且必须为正整数。

### R4 — 显式 Handler 注册

Handler 通过 scene id 到 `ISceneHandler<TFrame>` 的显式映射注册，不使用字符串反射。

某候选场景每次命中时均调用其 Handler，包括：

- 仍停留在当前稳定场景；
- 正处于 pending；
- 本帧确认转场。

Handler context 必须提供 frame、previous stable scene、current stable scene、原始 match 和 `IsTransitionConfirmed`。未注册 Handler 的场景走通用 fallback，不报错且不影响转场。

无候选命中而产生的 synthetic unknown 不调用 Handler；若调用方需要处理 unknown，应在 transition result 层处理，不把“没有命中”伪装为 matcher 命中。

### R5 — 配置验证

构造配置时必须拒绝：

- 空 initial/unknown scene；
- 缺少 initial 或 unknown transition row；
- 空候选列表；
- 空或重复候选；
- 指向没有 transition row 的候选；
- 非正数确认阈值；
- 空 Handler key、null Handler 或 Handler 对应不存在的 scene。

引擎不得持有调用方可继续修改的 transition list 或 Handler dictionary。

### R6 — 串行、取消与异步重置

同一引擎实例的 `ProcessAsync` 和 `ResetAsync` 必须进入同一串行队列，防止 pending 计数和稳定场景发生竞态。

- 等待处理队列和 matcher/Handler 执行均接受调用方 cancellation token。
- 取消不得提交未完成的转场或重置状态，也不得让后续队列项越过尚未完成的前序项。
- `ResetAsync()` 恢复 initial scene 并清除 pending。
- `ResetAsync(scene)` 只接受 transition matrix 中存在的非空 scene，并清除 pending。
- matcher 或 Handler 不得向同一引擎重入排队 `ProcessAsync`/`ResetAsync`；此类调用必须立即失败，而不是等待当前帧造成死锁。
- 重置 API 不得同步阻塞等待 in-flight matcher/Handler。

### R7 — Core 单向边界

`src/Wuwa.Core/SceneRecognition/**/*.cs` 不得依赖：

- WPF；
- `Wuwa.Infrastructure`；
- Native OCR/OpenCV；
- `OcrImageFrame`；
- `AchievementWorkspace`；
- 鸣潮具体场景、成就状态或持久化。

Core 通过泛型 `TFrame` 和 matcher/Handler 接口接受外部能力。

## 设计决策

### D1 — 泛型帧，而非绑定 OCR 帧

使用 `SceneTransitionEngine<TFrame>`。场景切换 Core 只编排候选和状态；后续由 Infrastructure 用 `OcrImageFrame` 实现 matcher。这样避免把通用场景状态机绑定到 OCR 命名和 BGR 布局。

### D2 — matcher 与 Handler 分离

matcher 只回答候选场景是否命中并返回置信度/附加数据；Handler 只处理命中后的调用方逻辑。场景引擎拥有候选顺序、首个命中和防抖规则。

### D3 — 每次命中调用 Handler

Handler 不仅是“转场通知”。逐帧 OCR、事件提取或场景内状态更新需要在稳定停留期间继续运行，因此每次命中均调用，并通过 context 标识本帧是否确认转场。

### D4 — synthetic unknown 不调用 Handler

没有候选命中和真实匹配 unknown 模板不是同一语义。首版把无命中作为 synthetic unknown，只参与防抖和结果输出，避免产生伪造的 match/Handler 数据。

### D5 — Core-only 首版

本 change 不创建生产 matcher 或具体场景。先固定公开行为和测试，后续 change 再接 Native/OpenCV 和鸣潮场景，降低同时改动算法、状态机和 OCR 流程的风险。

## Testing Decisions

### 最高层 seam

所有核心行为优先通过 `SceneTransitionEngine<TFrame>.ProcessAsync` 测试，不直接以私有计数器作为主要 seam。

### 必须自动验证

- 候选严格按配置顺序执行；
- 首个命中后停止；
- known 默认即时切换；
- unknown 连续两帧确认；
- pending 恢复、目标变化和异步重置；
- Handler 每次命中调用并收到正确 context；
- 未注册 Handler fallback；
- synthetic unknown 不调用 Handler；
- 配置防御性复制和无效配置拒绝；
- matcher 返回错误 scene、非有限置信度或 null 时可诊断失败；
- 取消不提交转场；
- 并发调用按串行顺序完成；
- Core 不依赖 Infrastructure/WPF/OCR 具体类型；
- 完整测试和 Release build 通过。

## 风险与控制

| 风险 | 控制 |
|---|---|
| Handler 在 pending 阶段产生过早副作用 | context 明确暴露 `IsTransitionConfirmed`，调用方决定逐帧逻辑和一次性逻辑 |
| transition matrix 被外部修改 | 构造时防御性复制字典和候选数组 |
| 并发帧或重置破坏 pending 计数 | 单实例有序队列串行化，增加并发、取消屏障和 queued reset 测试 |
| 同步或重入 reset 与 callback 互相等待 | 仅提供异步 reset，并立即拒绝 matcher/Handler 对同一引擎的重入排队 |
| unknown 被当作真实模板命中 | synthetic unknown 不创建伪造 match，不调用 Handler |
| 首版 API 过早绑定 OCR | 使用泛型帧和 Core-only 接口 |
| 后续接入发现匹配与 Handler 边界不够 | 首版以最小公开 seam 和离线测试固化；生产接入留到独立 change |

## 回退策略

本 change 不接生产流程。若 API 设计不适合后续 matcher，删除 `SceneRecognition` Core 文件和对应测试即可完整回退，不涉及资源、用户数据、generation 或 UI 迁移。

## 成功标准

- `Wuwa.Core` 提供不依赖 OCR/WPF/Infrastructure 的场景切换核心；
- transition matrix、首个命中、known/unknown 防抖和 Handler 行为符合本 spec；
- 所有新增测试、完整 `dotnet test` 和 Release build 通过；
- 当前 Native OCR 和应用运行行为未改变；
- change 文档记录实现和 fresh verification 证据。
