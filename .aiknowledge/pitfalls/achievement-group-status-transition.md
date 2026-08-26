# 成就组状态必须经过统一 transition

## 不要这样做

不要把互斥组或累计进度链的某一行当作独立记录直接写状态，也不要把 `已占用` 当成普通的 `未完成` 或把组内所有行都算成独立完成数。

## 反例

OCR、导入或界面直接修改一个成就组成员的状态表，留下互斥成员仍为 `未完成`；或者完成累计进度链的高等级只标高等级，统计和重新打开后无法反映低等级阈值已跨过。

## 正例

所有来源都向 `AchievementWorkspace` 提交状态请求，由统一状态转换处理：互斥组完成一个成员时其它成员变为 `已占用`，重置组成员时按规则恢复；累计进度组完成某级时低级一并完成，重置时清除该级及更高级。统计按原始成就行计数，成就组只用于状态转换，并用独立测试数据覆盖真实库缺少成就组元数据的情况。

## 为什么不行

组状态表达的是业务约束，不是表格行的装饰字段。绕过状态转换会产生多个互相矛盾的选择、错误的完成率和无法从导入/OCR 恢复的状态。原生版还要求未知状态在输入边界被拒绝，而不是以默认值破坏成就组不变量。

## 适用前提

当任务涉及 `GroupId`、`已占用`、累计进度链、互斥成就、统计、JSON/Excel 导入、Wiki 成就组解析或 OCR 合并时适用。不适用于没有成就组元数据的普通成就；普通成就仍需走统一状态校验。

## 验证

回读 `src/Wuwa.Core/AchievementWorkspace.cs` 的 `ApplyStatusTransition` 和统计计算，及 `src/Wuwa.Core/Models.cs` 的 `ProgressStatus`。运行 `tests/Wuwa.Tests/AchievementWorkspaceTests.cs` 中的成就组转换/统计测试、`AchievementIdentityTests.ProgressStatus_ExposesExactlyTheFourCanonicalLabels`，并检查 `src/Wuwa.Infrastructure/WikiSources.cs` 与 `scripts/mark_progression_chains.py` 的成就组元数据生成。

## 重审条件

当成就组模型、状态集合或 Wiki 的成就组语义改变时，重新审查状态转换、统计计数和所有导入/OCR 入口；不能只更新界面标签。
