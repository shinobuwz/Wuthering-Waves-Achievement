# OCR 只能从已确认 preview 合并进度

## 不要这样做

不要在 OCR 每页识别后直接写用户进度，不要把低置信度/未知状态当作确定结果，也不要把取消、未访问分类或部分扫描结果自动补成“未完成”。

## 反例

扫描线程发现一个名称就调用 status save；或者扫描中途失败后把当前累积字典当成全量真相，直接覆盖 workspace，使本次没有识别到的已完成成就降级。

## 正例

扫描层只构造内存中的 immutable candidates 和 unmatched/ambiguous diagnostics，UI 展示一份 preview；用户明确确认后，再通过 `AchievementWorkspace.ApplyOcrPreviewAsync` 以一个新 revision 合并。默认阻止 `已完成` 被非完成 OCR 结果降级，取消或识别失败保持旧 revision。

## 为什么不行

OCR 既可能误识别名称，也可能把状态行配错、漏掉不可见条目或只扫描到部分页面。把观察结果直接当作权威状态会让一次局部失败破坏长期进度，也绕过成就组 transition 和统一 persistence。

## 适用前提

当任务涉及 Legacy 或 Native OCR、扫描线程、预览表格、取消、跨页面合并和保存按钮时适用。不适用于用户在成就管理页面进行的明确手动状态变更；手动变更仍必须走同一工作区状态规则。

## 验证

回读 `src/Wuwa.Core/OcrMatching.cs`、`OcrScanContracts.cs` 和 `AchievementWorkspace.ApplyOcrPreviewAsync`，确认 duplicate/ambiguous candidate、confirmation、completed downgrade 和 cancellation 分支。运行 `tests/Wuwa.Tests/OcrMatchingTests.cs` 的 `ApplyOcrPreview_RequiresConfirmationAndCommitsOneRevisionWithoutDowngrade`、`OcrScanServiceTests.cs` 的 cancellation/failure tests；Native 侧检查 OCR preview/apply 流程。

## 重审条件

当 OCR 具备经过 differential fixtures 证明的全量覆盖率，或产品改为支持自动后台同步时，重新审查“确认前不写入”和防降级策略；任何自动化仍需保留可回滚 revision。
