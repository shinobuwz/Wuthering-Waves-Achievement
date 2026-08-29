# Evidence：新增可复用的场景切换 Core

## Current Status

- change_id: `2026-08-30-add-scene-transition-core`
- branch: `feat/scene-transition-core`
- base: `1240c7384c3b842b5827c144cca814f320eb8245`
- status: verified_ready_for_commit_a
- implementation: Core、测试和说明已完成；未接入 Native/OCR/WPF 生产流程。

## Plan Decisions

### 2026-08-30 — 首版范围

- decision: 只实现 `Wuwa.Core` 场景切换 Core 和测试，不接 Native matcher、具体场景、WPF 或 OCR 流程。
- decision: 最高层 public behavior seam 为 `SceneTransitionEngine<TFrame>.ProcessAsync`。
- decision: 使用泛型帧输入，避免绑定 `OcrImageFrame`。

### 2026-08-30 — 转场与 Handler 行为

- decision: transition matrix 候选顺序属于行为契约，首个命中后停止。
- decision: known 默认 1 帧确认，synthetic unknown 默认 2 帧确认，均可配置。
- decision: 显式注册 Handler 在每次真实场景命中时调用，并通过 context 暴露本帧是否确认转场。
- decision: 无候选命中产生的 synthetic unknown 不调用 Handler。

### 2026-08-30 — Reset 改为异步队列操作 | implement

背景：初版使用同步 `Reset` 排队后阻塞等待前序帧。
事件：focused workflow review 证明 matcher/Handler 内调用同步 `Reset` 会与当前 `ProcessAsync` 形成永久等待，且 WPF 单线程上下文也存在阻塞风险。
结论：公开重置契约改为 `ResetAsync()`/`ResetAsync(scene)`，与帧处理共享有序队列；matcher/Handler 对同一引擎的重入排队立即失败。spec、测试和 README 同步更新。

## Repository Evidence

- 当前 Native 项目已有窗口捕获、BGR 帧、Native OpenCV 模板匹配、OCR 和帧差分析，但此前没有独立场景 transition engine。
- 新增 Core 仅依赖 .NET 基础库，通过泛型帧、`ISceneMatcher<TFrame>` 和 `ISceneHandler<TFrame>` 接受外部能力。
- OCR 结果写入仍必须经过已确认 `OcrScanPreview` 和 `AchievementWorkspace.ApplyOcrPreviewAsync`；本 change 未触及该边界。
- focused review receipt: `work/receipts/implementation-review.md`。

## Final Verification

覆盖范围：branch `feat/scene-transition-core`；base `1240c7384c3b842b5827c144cca814f320eb8245`；当前 change 全部代码、测试、文档和 OPSX artifacts。
review session：`codebase-audit-mteppr31-q3c5yj`
review round：1/1（一次 focused review + 一次批量修正 + focused closure）
receipt：`implementation-review-2026-08-30` / mode `codebase-audit` / read-only workflow evidence
scope：local / `src/Wuwa.Core/SceneRecognition/`, `tests/Wuwa.Tests/SceneTransitionEngineTests.cs`, current change root
acceptance scope：local
result：pass
next action：Commit A → knowledge finalize-plan/capture → Commit B cleanup
stop reason：none
residual risks：
- 本 change 未提供 Native/OpenCV matcher、鸣潮场景模板或生产接入；这是明确非目标。
- 4 个完整套件测试因需要 Native OCR 模型/实时 Wiki/显式 Windows 捕获环境而按既有条件跳过；与本 change 无关。
- `Dispose` 拒绝新入队操作，已接受操作继续 drain；引擎不持有非托管资源。
验证命令：
- `dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --filter SceneTransitionEngineTests --no-restore` → 17 passed, 0 failed, 0 skipped。
- `dotnet test WutheringWavesAchievement.sln -c Release --no-restore` → 99 passed, 0 failed, 4 skipped。
- `dotnet build WutheringWavesAchievement.sln -c Release --no-restore` → success, 0 warnings, 0 errors。
- `rg -n "Wuwa\.Infrastructure|System\.Windows|OcrImageFrame|AchievementWorkspace|OpenCv|OpenCV" src/Wuwa.Core/SceneRecognition -g '*.cs'` → 仅 XML 注释出现 `OpenCV`，无禁止依赖或具体类型引用。
- `git diff --check` → pass。
spec 合规：R1–R7 均由 public seam 测试、边界扫描和代码审查覆盖；所有 task checkbox 已完成。
release risk：新增未接入生产的 Core public API；无 schema、持久化、resource、Native ABI 或现有调用方变更。review 发现的 reset deadlock 已修正并有重入/queued reset 回归测试。
结论：通过；满足 Commit A 条件。
