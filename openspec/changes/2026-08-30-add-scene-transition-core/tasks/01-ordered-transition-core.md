# 01 — 建立有序场景切换闭环

**What to build:** 在 `Wuwa.Core` 中新增泛型 scene matcher、transition options、逐帧结果和 `SceneTransitionEngine<TFrame>`。引擎按当前稳定场景对应的候选顺序执行 matcher，在首个命中后停止；没有候选命中时生成 synthetic unknown；known 和 unknown 使用独立确认阈值。

**Blocked by:** None — can start immediately

**Suggested Files:**

- `src/Wuwa.Core/SceneRecognition/SceneRecognitionContracts.cs`
- `src/Wuwa.Core/SceneRecognition/SceneTransitionEngine.cs`
- `tests/Wuwa.Tests/SceneTransitionEngineTests.cs`

**Behavior Context:**

```text
current stable scene
→ ordered candidates
→ matcher(candidate 1)
→ ...
→ first match or synthetic unknown
→ pending/confirmation update
→ immutable result
```

候选顺序不得重排。pending 期间重新看到当前稳定场景会清除 pending；pending 目标变化会从 1 重新计数。

**Acceptance:**

- 配置和结果为公开 Core 契约；
- 已知场景默认 1 帧确认；
- unknown 默认 2 帧确认；
- 首个命中后不执行后续 matcher；
- 无效 transition matrix 在构造时失败；
- 单元测试覆盖正常切换、稳定场景 row、unknown、防抖恢复和 pending 目标直接变化。

**Verification:**

```bash
dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --filter SceneTransitionEngineTests
```
