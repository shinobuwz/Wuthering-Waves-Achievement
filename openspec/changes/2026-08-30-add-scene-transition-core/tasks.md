# Tasks：新增可复用的场景切换 Core

- [x] [01 — 建立有序场景切换闭环](tasks/01-ordered-transition-core.md)
  - **Blocked by:** None — can start immediately
  - **Suggested Files:** `src/Wuwa.Core/SceneRecognition/`, `tests/Wuwa.Tests/SceneTransitionEngineTests.cs`
  - **Acceptance:** `ProcessAsync` 按 transition matrix 顺序执行 matcher、首个命中停止，并按 known/unknown 阈值输出稳定场景和 pending 状态。
  - **Verification:** `dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --filter SceneTransitionEngineTests`

- [x] [02 — 固化 Handler、取消和并发契约](tasks/02-handler-and-robustness.md)
  - **Blocked by:** Task 01
  - **Suggested Files:** `src/Wuwa.Core/SceneRecognition/`, `tests/Wuwa.Tests/SceneTransitionEngineTests.cs`
  - **Acceptance:** 显式 Handler 每次命中调用，fallback、synthetic unknown、重置、取消、防御性复制和串行并发行为均有自动测试。
  - **Verification:** `dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --filter SceneTransitionEngineTests`

- [x] [03 — 文档、边界扫描和完整验证](tasks/03-document-and-verify.md)
  - **Blocked by:** Task 02
  - **Suggested Files:** `src/Wuwa.Core/SceneRecognition/README.md`, `openspec/changes/2026-08-30-add-scene-transition-core/evidence.md`, `openspec/changes/2026-08-30-add-scene-transition-core/work/checkpoint.md`
  - **Acceptance:** 使用说明和后续接入边界明确；Core 无 Infrastructure/WPF/OCR 具体类型依赖；完整测试与 Release build fresh pass；evidence/checkpoint 更新。
  - **Verification:** `dotnet test WutheringWavesAchievement.sln -c Release`、`dotnet build WutheringWavesAchievement.sln -c Release --no-restore`、依赖边界扫描。
