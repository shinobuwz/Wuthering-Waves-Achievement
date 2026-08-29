# 02 — 固化 Handler、取消和并发契约

**What to build:** 在 Task 01 的完整转场 seam 上加入显式 Handler 注册、每次命中调用、未注册 fallback、synthetic unknown 行为、异步重置、取消和单实例有序队列处理。补充 matcher/Handler 异常数据的可诊断失败、重入拒绝和配置防御性复制。

**Blocked by:** Task 01

**Suggested Files:**

- `src/Wuwa.Core/SceneRecognition/SceneRecognitionContracts.cs`
- `src/Wuwa.Core/SceneRecognition/SceneTransitionEngine.cs`
- `tests/Wuwa.Tests/SceneTransitionEngineTests.cs`

**Behavior Context:**

Handler 每次真实候选命中均调用。context 同时携带处理前稳定场景、处理后稳定场景、原始 match 和 `IsTransitionConfirmed`。无候选命中产生的 synthetic unknown 不调用 Handler。

同一引擎实例的并发 `ProcessAsync`/`ResetAsync` 必须按入队顺序串行提交状态；取消等待或取消 matcher/Handler 时不得提前提交状态，也不得让后续队列项越过未完成的前序项。matcher/Handler 对同一引擎的重入排队必须立即失败，避免 callback 与当前帧互相等待。

**Acceptance:**

- Handler 仅通过显式 scene-id 映射注册；
- 稳定停留、pending 和确认转场帧均调用 Handler；
- 未注册 Handler 正常 fallback；
- synthetic unknown 不调用 Handler；
- `ResetAsync()`/`ResetAsync(scene)` 清理 pending，空或未知 scene 被拒绝；
- matcher/Handler 重入排队立即失败；
- 取消、队列屏障和并发测试通过；
- 外部修改原配置集合不会改变引擎行为；
- matcher 返回 scene 不一致、null 或非有限置信度时失败信息明确。

**Verification:**

```bash
dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --filter SceneTransitionEngineTests
```
