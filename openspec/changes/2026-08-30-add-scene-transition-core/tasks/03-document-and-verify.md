# 03 — 文档、边界扫描和完整验证

**What to build:** 为 Core 编写最小使用说明、复制/依赖边界和后续 Infrastructure matcher 接入说明。在最后一次代码修改后执行聚焦测试、完整测试、Release build 和依赖边界扫描，并把当前 snapshot 的结果写入 change evidence/checkpoint。

**Blocked by:** Task 02

**Suggested Files:**

- `src/Wuwa.Core/SceneRecognition/README.md`
- `openspec/changes/2026-08-30-add-scene-transition-core/evidence.md`
- `openspec/changes/2026-08-30-add-scene-transition-core/work/checkpoint.md`

**Acceptance:**

- README 包含最小 transition matrix、matcher、Handler 和 `ProcessAsync` 示例；
- 明确本 change 不包含模板、Native adapter、WPF/OCR 接入和进度写入；
- SceneRecognition Core 不导入 Infrastructure/WPF/OCR 具体类型；
- 聚焦测试、完整测试和 Release build fresh pass；
- evidence 记录命令、结果、changed paths 和残余风险；
- checkpoint 更新为可交付状态。

**Verification:**

```bash
dotnet test tests/Wuwa.Tests/Wuwa.Tests.csproj -c Release --filter SceneTransitionEngineTests
dotnet test WutheringWavesAchievement.sln -c Release
dotnet build WutheringWavesAchievement.sln -c Release --no-restore
rg -n "Wuwa\.Infrastructure|System\.Windows|OcrImageFrame|AchievementWorkspace" src/Wuwa.Core/SceneRecognition -g "*.cs"
git diff --name-only
```
